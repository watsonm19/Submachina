using System;
using UnityEditor;
using UnityEngine;

namespace SynapticPro
{
    /// <summary>
    /// Window to manage operation history and Undo/Redo
    /// </summary>
    public class NexusHistoryWindow : EditorWindow
    {
        // [MenuItem("Window/Synaptic Pro/📜 Operation History")]
        public static void ShowWindow()
        {
            var window = GetWindow<NexusHistoryWindow>("📜 Operation History");
            window.minSize = new Vector2(300, 400);
            window.Show();
        }

        private Vector2 scrollPosition;
        private bool autoRefresh = true;

        private void OnEnable()
        {
            NexusOperationHistory.Instance.OnHistoryChanged += Repaint;
        }

        private void OnDisable()
        {
            NexusOperationHistory.Instance.OnHistoryChanged -= Repaint;
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawControls();
            DrawHistoryInfo();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Operation History", EditorStyles.boldLabel);

            GUILayout.FlexibleSpace();

            autoRefresh = GUILayout.Toggle(autoRefresh, "Auto Refresh", EditorStyles.toolbarButton, GUILayout.Width(90));

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                Repaint();
            }
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawControls()
        {
            EditorGUILayout.BeginHorizontal();

            // Undo button
            GUI.enabled = NexusOperationHistory.Instance.CanUndo;
            if (GUILayout.Button("↶ Undo", GUILayout.Height(30)))
            {
                if (NexusOperationHistory.Instance.Undo())
                {
                    EditorUtility.DisplayDialog("Undo", "Operation undone", "OK");
                }
            }

            // Redo button
            GUI.enabled = NexusOperationHistory.Instance.CanRedo;
            if (GUILayout.Button("↷ Redo", GUILayout.Height(30)))
            {
                if (NexusOperationHistory.Instance.Redo())
                {
                    EditorUtility.DisplayDialog("Redo", "Operation redone", "OK");
                }
            }
            
            GUI.enabled = true;
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();

            // Clear history
            if (GUILayout.Button("Clear History", GUILayout.Height(25)))
            {
                if (EditorUtility.DisplayDialog("Confirm", "Delete all history?", "Delete", "Cancel"))
                {
                    NexusOperationHistory.Instance.ClearHistory();
                }
            }

            // Export
            if (GUILayout.Button("Export", GUILayout.Height(25)))
            {
                ExportHistory();
            }
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(10);
        }

        private void DrawHistoryInfo()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            var info = NexusOperationHistory.Instance.GetHistoryInfo();
            
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            
            EditorGUILayout.TextArea(info, GUILayout.ExpandHeight(true));
            
            EditorGUILayout.EndScrollView();
            
            EditorGUILayout.EndVertical();
        }

        private void ExportHistory()
        {
            var path = EditorUtility.SaveFilePanel(
                "Export History",
                Application.dataPath,
                $"NexusHistory_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                "json"
            );

            if (!string.IsNullOrEmpty(path))
            {
                var json = NexusOperationHistory.Instance.ExportHistory();
                System.IO.File.WriteAllText(path, json);
                EditorUtility.DisplayDialog("Export Complete", "History has been exported", "OK");
            }
        }
    }
}