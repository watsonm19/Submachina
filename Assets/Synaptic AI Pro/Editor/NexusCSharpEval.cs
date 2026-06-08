using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json;

namespace SynapticPro
{
    /// <summary>
    /// Arbitrary C# evaluation for the Editor — equivalent of Blender's
    /// run_python tool. Wraps Mono.CSharp.Evaluator (instance API) so callers
    /// can execute any C# snippet against the running Editor without
    /// triggering an AssemblyReload.
    ///
    /// Static Evaluator.Init/Run on Unity 2022.3+ silently no-ops; the real
    /// path is `new Evaluator(new CompilerContext(new CompilerSettings(),
    /// new ConsoleReportPrinter()))` plus injecting every assembly already
    /// loaded in the AppDomain so UnityEngine / UnityEditor / Newtonsoft.Json
    /// resolve.
    ///
    /// Expressions ("1+1", "GameObject.Find(\"X\").name") must NOT end with
    /// a semicolon — those are evaluated via Evaluate(...). Statements
    /// ("var x = 1; Debug.Log(x);") run through Run(...).
    /// </summary>
    public static class NexusCSharpEval
    {
        private static object _evaluator;
        private static MethodInfo _evaluateMethod;
        private static MethodInfo _runMethod;
        private static MethodInfo _referenceAssemblyMethod;
        private static StringBuilder _captured = new StringBuilder();
        private static readonly object _lock = new object();
        // ESC-0107 fix (E): capture Unity Debug.Log output that Console.SetOut
        // can't catch (UnityEngine.Debug routes through Unity Console, not
        // managed Console.Out). Subscribe to Application.logMessageReceived
        // while a Run call is in progress.
        private static bool _captureUnityLogs = false;
        private static Application.LogCallback _logCallback;

        // ESC-0107 fix (D, revised): receive the return value from `return X;`
        // snippets via a static field. The user's `return X;` is rewritten to
        // `SynapticPro.NexusCSharpEval.__SetResult(X);` and executed through
        // Evaluator.Run, which lets Mono.CSharp's normal method-body parser
        // handle the expression (no pointer-type ambiguity, no Evaluate
        // restrictions). We then read the field back in managed code.
        public static object __LastResult;
        public static bool __LastResultSet;

        public static void __SetResult(object value)
        {
            __LastResult = value;
            __LastResultSet = true;
        }

        public static string Run(Dictionary<string, string> parameters)
        {
            var code = parameters != null && parameters.TryGetValue("code", out var c) ? c : "";
            if (string.IsNullOrEmpty(code))
            {
                return JsonConvert.SerializeObject(new
                {
                    success = false,
                    error = "code parameter is required",
                    example = "GameObject.Find(\"Cube\")?.name"
                });
            }

            lock (_lock)
            {
                try
                {
                    if (!EnsureInitialized(out var initError))
                    {
                        return JsonConvert.SerializeObject(new
                        {
                            success = false,
                            error = initError
                        });
                    }

                    _captured.Length = 0;
                    var oldOut = Console.Out;
                    Console.SetOut(new StringWriter(_captured));
                    // Hook Unity Debug.Log into the capture buffer (ESC-0107 fix E).
                    AttachUnityLogCapture();
                    try
                    {
                        // ESC-0107 fix D (revised again): rewrite the trailing
                        // `return X;` into a call to our static `__SetResult(X)`
                        // sink, then Run the whole thing as statements. This
                        // sidesteps two Mono.CSharp quirks:
                        //   - Run(...) discards real `return` values
                        //   - Evaluate("x * 1") mis-parses var*literal as a
                        //     pointer-type declaration, returning resultSet=false
                        // Inside a normal Run body the parser treats `*` as
                        // unambiguous multiplication and `__SetResult(X)` as a
                        // regular method call. The receiver field is read back
                        // in managed code after Run completes.
                        var trimmed = code.TrimEnd();
                        SplitReturnTail(trimmed, out var prefixStatements, out var returnExpression);

                        // For bare expressions (no `;`) we still use Evaluate
                        // — it works fine for that case and avoids a needless
                        // method-call wrap.
                        string expressionToEvaluate = null;
                        string rewrittenStatements = null;
                        if (returnExpression != null)
                        {
                            rewrittenStatements = string.IsNullOrEmpty(prefixStatements)
                                ? $"SynapticPro.NexusCSharpEval.__SetResult({returnExpression});"
                                : $"{prefixStatements} SynapticPro.NexusCSharpEval.__SetResult({returnExpression});";
                        }
                        else if (!trimmed.EndsWith(";"))
                        {
                            expressionToEvaluate = code;
                        }

                        // Reset the sink before every call so a stale value
                        // from a previous run can't leak through.
                        __LastResult = null;
                        __LastResultSet = false;

                        if (rewrittenStatements != null && _runMethod != null)
                        {
                            // First try plain Run. Works for most snippets and
                            // is the cheapest path. If it succeeds but doesn't
                            // set the result (e.g. the user's code contains
                            // generic type syntax `List<int>` which Mono.CSharp
                            // Evaluator's top-level parser chokes on), retry
                            // wrapped in an immediately-invoked Action lambda
                            // — Mono parses the lambda body with the regular
                            // method-body parser, which handles generics fine.
                            object runResultR;
                            try { runResultR = _runMethod.Invoke(_evaluator, new object[] { rewrittenStatements }); }
                            catch (TargetInvocationException tie)
                            {
                                return Error(tie.InnerException ?? tie);
                            }
                            bool runOkR = runResultR is bool rbR && rbR;

                            if (!__LastResultSet)
                            {
                                // Reset for the wrapped retry (output already
                                // captured what the failed attempt printed,
                                // which should be nothing — Run prints nothing
                                // on parse failure).
                                __LastResult = null;
                                __LastResultSet = false;
                                var wrapped = $"((System.Action)(() => {{ {rewrittenStatements} }}))();";
                                try { runResultR = _runMethod.Invoke(_evaluator, new object[] { wrapped }); }
                                catch (TargetInvocationException tie)
                                {
                                    return Error(tie.InnerException ?? tie);
                                }
                                runOkR = runResultR is bool rbR2 && rbR2;
                            }

                            return JsonConvert.SerializeObject(new
                            {
                                success = runOkR,
                                output = _captured.ToString(),
                                result = __LastResultSet ? SafeStringify(__LastResult) : null,
                                resultSet = __LastResultSet
                            });
                        }

                        if (expressionToEvaluate != null && _evaluateMethod != null)
                        {
                            var args = new object[] { expressionToEvaluate, null, false };
                            object remainderObj = null;
                            try { remainderObj = _evaluateMethod.Invoke(_evaluator, args); }
                            catch (TargetInvocationException tie)
                            {
                                return Error(tie.InnerException ?? tie);
                            }

                            string remainder = remainderObj as string ?? "";
                            object result = args[1];
                            bool resultSet = args[2] is bool b && b;

                            if (string.IsNullOrEmpty(remainder))
                            {
                                return JsonConvert.SerializeObject(new
                                {
                                    success = true,
                                    output = _captured.ToString(),
                                    result = resultSet ? SafeStringify(result) : null,
                                    resultSet
                                });
                            }
                            // Evaluator returned remainder — the "expression" we
                            // extracted was actually parsed as statements. Fall
                            // through to statement-only path for the full input.
                        }

                        // Pure-statement input (no return form, no bare expression)
                        // — fall through to plain Run. Captures stdout but no
                        // value. Note: with the lambda-wrap approach above we
                        // never executed the prefix as a side effect already,
                        // so the original `code` is safe to Run here as-is.

                        // Statement mode.
                        if (_runMethod == null)
                        {
                            return JsonConvert.SerializeObject(new
                            {
                                success = false,
                                error = "Evaluator.Run method not found on this Mono.CSharp build"
                            });
                        }

                        object runResult;
                        try { runResult = _runMethod.Invoke(_evaluator, new object[] { code }); }
                        catch (TargetInvocationException tie)
                        {
                            return Error(tie.InnerException ?? tie);
                        }

                        bool runOk = runResult is bool rb && rb;
                        // Also surface __SetResult writes from user code, even
                        // when we didn't auto-rewrite. Lets advanced callers do
                        // their own SetResult invocation (e.g. inside lambdas
                        // when avoiding the Evaluator generic-parse bug).
                        if (__LastResultSet)
                        {
                            return JsonConvert.SerializeObject(new
                            {
                                success = runOk,
                                output = _captured.ToString(),
                                result = SafeStringify(__LastResult),
                                resultSet = true
                            });
                        }
                        return JsonConvert.SerializeObject(new
                        {
                            success = runOk,
                            output = _captured.ToString(),
                            result = (object)null
                        });
                    }
                    finally
                    {
                        Console.SetOut(oldOut);
                        DetachUnityLogCapture();
                    }
                }
                catch (Exception e)
                {
                    return Error(e);
                }
            }
        }

        private static bool EnsureInitialized(out string error)
        {
            error = null;
            if (_evaluator != null) return true;

            Assembly mcs = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Mono.CSharp");
            if (mcs == null)
            {
                try { mcs = Assembly.Load("Mono.CSharp"); } catch { /* try alt below */ }
            }
            if (mcs == null)
            {
                error = "Mono.CSharp.dll is not loaded in this Unity build. " +
                        "Add an .asmdef reference to Mono.CSharp or use a Unity " +
                        "version that bundles it (most Unity LTS releases do).";
                return false;
            }

            Type settingsType = mcs.GetType("Mono.CSharp.CompilerSettings");
            Type printerType  = mcs.GetType("Mono.CSharp.ConsoleReportPrinter");
            Type reportType   = mcs.GetType("Mono.CSharp.Report");
            Type contextType  = mcs.GetType("Mono.CSharp.CompilerContext");
            Type evalType     = mcs.GetType("Mono.CSharp.Evaluator");

            if (settingsType == null || printerType == null || contextType == null || evalType == null)
            {
                error = "Mono.CSharp internal types missing " +
                        $"(settings={settingsType != null}, printer={printerType != null}, " +
                        $"context={contextType != null}, eval={evalType != null}).";
                return false;
            }

            object settings = Activator.CreateInstance(settingsType);
            object printer  = Activator.CreateInstance(printerType);

            // Try CompilerContext(CompilerSettings, ReportPrinter)
            object ctx = null;
            ConstructorInfo ctxCtor = contextType.GetConstructors()
                .FirstOrDefault(ci => ci.GetParameters().Length == 2);
            if (ctxCtor != null)
            {
                try { ctx = ctxCtor.Invoke(new object[] { settings, printer }); }
                catch { ctx = null; }
            }

            if (ctx == null)
            {
                error = "Could not construct Mono.CSharp.CompilerContext.";
                return false;
            }

            ConstructorInfo evalCtor = evalType.GetConstructor(new[] { contextType });
            if (evalCtor == null)
            {
                error = "Mono.CSharp.Evaluator(CompilerContext) constructor not found.";
                return false;
            }
            _evaluator = evalCtor.Invoke(new object[] { ctx });

            _evaluateMethod = evalType.GetMethod(
                "Evaluate",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(string), typeof(object).MakeByRefType(), typeof(bool).MakeByRefType() },
                null);

            _runMethod = evalType.GetMethod(
                "Run",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(string) },
                null);

            _referenceAssemblyMethod = evalType.GetMethod(
                "ReferenceAssembly",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Assembly) },
                null);

            // Inject every already-loaded assembly so user code can reach
            // UnityEngine / UnityEditor / Newtonsoft.Json / the project's
            // own scripts without manual `using`.
            if (_referenceAssemblyMethod != null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (asm.IsDynamic) continue;
                        if (string.IsNullOrEmpty(asm.Location)) continue;
                        _referenceAssemblyMethod.Invoke(_evaluator, new object[] { asm });
                    }
                    catch { /* skip individual failures */ }
                }
            }

            // Pre-import common namespaces.
            if (_runMethod != null)
            {
                try
                {
                    _runMethod.Invoke(_evaluator, new object[]
                    {
                        "using System; " +
                        "using System.Collections.Generic; " +
                        "using System.Linq; " +
                        "using System.IO; " +
                        "using UnityEngine; " +
                        "using UnityEditor; " +
                        "using Newtonsoft.Json;"
                    });
                }
                catch { /* best-effort */ }
            }

            return true;
        }

        private static string Error(Exception e)
        {
            return JsonConvert.SerializeObject(new
            {
                success = false,
                error = e.Message,
                stackTrace = e.StackTrace
            });
        }

        private static object SafeStringify(object value)
        {
            if (value == null) return null;
            try
            {
                var t = value.GetType();
                if (t.IsPrimitive || value is string) return value;
                if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return value.ToString();
                return JsonConvert.SerializeObject(value);
            }
            catch
            {
                return value?.ToString();
            }
        }

        /// <summary>
        /// Split `[statements...] return X;` into (prefix, expression).
        /// Locates the LAST top-level `return` keyword (depth 0 from braces,
        /// parens, brackets, strings and comments) and splits there.
        ///
        /// Earlier implementations split on the last top-level `;` and then
        /// checked that the resulting final statement started with `return`.
        /// That broke on inputs like `foreach (var x in xs) { X(); } return Y;`
        /// because the only top-level `;` is the trailing one (the `X();`
        /// inside braces is at depth 1) so the "final statement" became the
        /// entire body including the foreach — not starting with `return`.
        ///
        /// The new approach: scan for `return` itself at depth 0, take
        /// everything before it as the prefix (its predecessor will end in
        /// `;` or `}` — both are valid statement terminators that Mono.CSharp
        /// Evaluator.Run accepts), take everything between `return` and the
        /// trailing `;` as the expression.
        /// </summary>
        private static void SplitReturnTail(string trimmed, out string prefix, out string expression)
        {
            prefix = "";
            expression = null;
            if (string.IsNullOrEmpty(trimmed)) return;
            if (!trimmed.EndsWith(";")) return;

            var withoutSemi = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();

            int returnIdx = FindLastTopLevelReturnKeyword(withoutSemi);
            if (returnIdx < 0) return;

            // Expression is the slice after the `return` keyword.
            var expr = withoutSemi.Substring(returnIdx + "return".Length).Trim();
            if (string.IsNullOrEmpty(expr)) return;

            expression = expr;
            if (returnIdx > 0)
            {
                // Prefix is everything before `return`. Validate it terminates
                // with `;` or `}` — anything else (e.g. half-written input)
                // would be malformed, in which case skip the rewrite.
                var candidatePrefix = withoutSemi.Substring(0, returnIdx).TrimEnd();
                if (!candidatePrefix.EndsWith(";") && !candidatePrefix.EndsWith("}"))
                {
                    expression = null;
                    return;
                }
                prefix = candidatePrefix;
            }
        }

        /// <summary>
        /// Find the start index of the LAST occurrence of the `return`
        /// keyword in <paramref name="s"/> that sits at the top level
        /// (outside any (), [], {} group, and outside string/char/comment
        /// literals). Also enforces token boundaries so `Return`,
        /// `myReturn`, `returnX` etc. don't match.
        ///
        /// Returns -1 when no such keyword exists.
        /// </summary>
        private static int FindLastTopLevelReturnKeyword(string s)
        {
            const string KW = "return";
            int paren = 0, bracket = 0, brace = 0;
            int lastReturn = -1;
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];

                // Line comment // ...
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    while (i < s.Length && s[i] != '\n') i++;
                    continue;
                }
                // Block comment /* ... */
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
                    i += 2;
                    continue;
                }
                // String / verbatim / interpolated — skip until closing quote.
                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    bool verbatim = i > 0 && (s[i - 1] == '@' || s[i - 1] == '$');
                    i++;
                    while (i < s.Length)
                    {
                        if (!verbatim && s[i] == '\\') { i += 2; continue; }
                        if (s[i] == quote)
                        {
                            if (verbatim && i + 1 < s.Length && s[i + 1] == quote) { i += 2; continue; }
                            i++;
                            break;
                        }
                        i++;
                    }
                    continue;
                }
                if (c == '(') { paren++; i++; continue; }
                if (c == ')') { paren--; i++; continue; }
                if (c == '[') { bracket++; i++; continue; }
                if (c == ']') { bracket--; i++; continue; }
                if (c == '{') { brace++; i++; continue; }
                if (c == '}') { brace--; i++; continue; }

                // Check for `return` keyword start at this position.
                if (paren == 0 && bracket == 0 && brace == 0 &&
                    c == KW[0] && i + KW.Length <= s.Length &&
                    s.Substring(i, KW.Length) == KW)
                {
                    // Left boundary: must be start of string OR a non-identifier
                    // char (whitespace, `}`, `;`, `{`, etc.).
                    bool leftOk = (i == 0) || !IsIdentChar(s[i - 1]);
                    // Right boundary: must be whitespace or `(` after the keyword.
                    bool rightOk = false;
                    if (i + KW.Length < s.Length)
                    {
                        var nxt = s[i + KW.Length];
                        rightOk = !IsIdentChar(nxt);
                    }
                    else
                    {
                        rightOk = true; // EOF immediately after — `return` with no expr (handled later)
                    }
                    if (leftOk && rightOk)
                    {
                        lastReturn = i;
                        i += KW.Length;
                        continue;
                    }
                }

                i++;
            }
            return lastReturn;
        }

        private static bool IsIdentChar(char c)
        {
            return c == '_' || char.IsLetterOrDigit(c);
        }

        private static void AttachUnityLogCapture()
        {
            if (_captureUnityLogs) return;
            _captureUnityLogs = true;
            _logCallback = (string condition, string stackTrace, LogType type) =>
            {
                // Mirror Debug.Log / LogWarning / LogError into _captured so the
                // caller sees what their script printed (NexusCSharpEval is the
                // only writer to _captured during a Run call, no contention).
                try
                {
                    var prefix = type == LogType.Error || type == LogType.Exception ? "[error] "
                              : type == LogType.Warning ? "[warn] "
                              : "";
                    _captured.Append(prefix).Append(condition).Append('\n');
                }
                catch { /* best-effort, never throw from log hook */ }
            };
            Application.logMessageReceived += _logCallback;
        }

        private static void DetachUnityLogCapture()
        {
            if (!_captureUnityLogs) return;
            try
            {
                if (_logCallback != null) Application.logMessageReceived -= _logCallback;
            }
            catch { }
            _captureUnityLogs = false;
            _logCallback = null;
        }
    }
}
