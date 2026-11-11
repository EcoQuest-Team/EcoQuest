# EcoQuest QA Tooling Removal Guide

This folder (`Assets/EcoQuest/Editor/QA/`) and any runtime companion folder (`Assets/EcoQuest/QA/`) contain read-only automated testing, diagnostic, and QA auditing harnesses created specifically for the comprehensive EcoQuest QA audit.

### How to Completely Remove QA Tooling
To remove all QA tooling and restore the repository to 100% untouched production state:
1. In the Unity Project window, delete:
   - `Assets/EcoQuest/Editor/QA/` (and its `.meta` file)
   - `Assets/EcoQuest/QA/` (if present, and its `.meta` file)
2. Or in Git / PowerShell:
   ```bash
   git clean -fd Assets/EcoQuest/Editor/QA/ Assets/EcoQuest/QA/
   ```
3. Unity will automatically recompile `Assembly-CSharp-Editor`. No production game code, scenes, or assets are modified by this tooling.
