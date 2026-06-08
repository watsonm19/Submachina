#!/usr/bin/env python3
import re

# Read essential tools list
with open('essential-tools-list.txt', 'r') as f:
    essential_tools = set(line.strip() for line in f if line.strip())

print(f"Essential tools count: {len(essential_tools)}")

# Read index.js
with open('index.js', 'r') as f:
    content = f.read()

# Split by tool registrations
# Pattern: mcpServer.registerTool('tool_name', ...);
pattern = r"(    mcpServer\.registerTool\('([^']+)',[\s\S]*?\n    \}\);)"

matches = list(re.finditer(pattern, content))
print(f"Found {len(matches)} tool registrations")

# Find the start of tool registrations
first_match_start = matches[0].start() if matches else -1
last_match_end = matches[-1].end() if matches else -1

# Extract prefix (before first tool), suffix (after last tool), and tools
prefix = content[:first_match_start]
suffix = content[last_match_end:]

# Filter tools
filtered_tools = []
removed_count = 0
kept_count = 0

for match in matches:
    tool_block = match.group(1)
    tool_name = match.group(2)

    if tool_name in essential_tools:
        filtered_tools.append(tool_block)
        kept_count += 1
        print(f"✓ Keeping: {tool_name}")
    else:
        removed_count += 1
        print(f"✗ Removing: {tool_name}")

# Reconstruct file
output = prefix + '\n\n'.join(filtered_tools) + suffix

# Write to index-essential.js
with open('index-essential.js', 'w') as f:
    f.write(output)

print(f"\n=== Summary ===")
print(f"Kept: {kept_count} tools")
print(f"Removed: {removed_count} tools")
print(f"Output written to: index-essential.js")
