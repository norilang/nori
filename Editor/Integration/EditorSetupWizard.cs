#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Nori
{
    public class EditorSetupWizard : EditorWindow
    {
        private enum EditorTab { VSCode, Rider, VisualStudio }

        private EditorTab _selectedTab;
        private string _lspBinaryPath;
        private bool _isBuilding;
        private string _buildOutput;
        private bool _dotnetAvailable;
        private string _dotnetVersion;
        private Vector2 _scrollPos;

        private const string LspBinaryPathPref = "Nori_LspBinaryPath";

        public static void ShowWindow()
        {
            var window = GetWindow<EditorSetupWizard>("Nori Editor Setup");
            window.minSize = new Vector2(450, 400);
            window.maxSize = new Vector2(450, 400);
        }

        private void OnEnable()
        {
            _lspBinaryPath = EditorPrefs.GetString(LspBinaryPathPref, "");
            CheckDotnetSdk();
        }

        private void CheckDotnetSdk()
        {
            try
            {
                var psi = new ProcessStartInfo("dotnet", "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var proc = Process.Start(psi);
                _dotnetVersion = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                int majorVersion = 0;
                if (_dotnetVersion.Length > 0)
                    int.TryParse(_dotnetVersion.Split('.')[0], out majorVersion);
                _dotnetAvailable = proc.ExitCode == 0 && majorVersion >= 8;
            }
            catch
            {
                _dotnetAvailable = false;
                _dotnetVersion = "not found";
            }
        }

        private string GetPackageRoot()
        {
            // This script is at Editor/Integration/EditorSetupWizard.cs
            string scriptPath = AssetDatabase.GetAssetPath(
                MonoScript.FromScriptableObject(this));
            if (!string.IsNullOrEmpty(scriptPath))
            {
                // scriptPath: Packages/dev.nori.compiler/Editor/Integration/EditorSetupWizard.cs
                string fullPath = Path.GetFullPath(scriptPath);
                return Path.GetFullPath(Path.Combine(fullPath, "..", "..", ".."));
            }
            // Fallback: search for the tools~ directory
            string candidate = Path.GetFullPath("Packages/dev.nori.compiler");
            if (Directory.Exists(candidate))
                return candidate;
            return null;
        }

        private string GetRuntimeIdentifier()
        {
#if UNITY_EDITOR_WIN
            return "win-x64";
#elif UNITY_EDITOR_OSX
            return System.Runtime.InteropServices.RuntimeInformation.OSArchitecture ==
                   System.Runtime.InteropServices.Architecture.Arm64
                ? "osx-arm64" : "osx-x64";
#else
            return "linux-x64";
#endif
        }

        private string GetExpectedBinaryName()
        {
#if UNITY_EDITOR_WIN
            return "nori-lsp.exe";
#else
            return "nori-lsp";
#endif
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Nori Editor Setup", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // .NET SDK status
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(".NET SDK:", GUILayout.Width(70));
            if (_dotnetAvailable)
                EditorGUILayout.LabelField($"{_dotnetVersion}", EditorStyles.miniLabel);
            else
                EditorGUILayout.LabelField($"{_dotnetVersion}", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            if (!_dotnetAvailable)
            {
                EditorGUILayout.HelpBox(
                    ".NET 8 SDK is required to build the LSP server. " +
                    "Download it from https://dotnet.microsoft.com/download/dotnet/8.0",
                    MessageType.Warning);
            }

            // LSP binary status
            bool hasBinary = !string.IsNullOrEmpty(_lspBinaryPath) && File.Exists(_lspBinaryPath);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("LSP Server:", GUILayout.Width(70));
            EditorGUILayout.LabelField(
                hasBinary ? Path.GetFileName(_lspBinaryPath) : "not built",
                EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            // Build button
            EditorGUI.BeginDisabledGroup(!_dotnetAvailable || _isBuilding);
            if (GUILayout.Button(_isBuilding ? "Building..." : "Build LSP Server", GUILayout.Height(28)))
            {
                BuildLspServer();
            }
            EditorGUI.EndDisabledGroup();

            if (hasBinary)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.TextField("Binary path:", _lspBinaryPath);
                if (GUILayout.Button("Copy", GUILayout.Width(50)))
                {
                    EditorGUIUtility.systemCopyBuffer = _lspBinaryPath;
                    Debug.Log("[Nori] LSP server path copied to clipboard.");
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(8);
            DrawSeparator();
            EditorGUILayout.Space(4);

            // Editor tabs
            _selectedTab = (EditorTab)GUILayout.Toolbar(
                (int)_selectedTab,
                new[] { "VS Code", "Rider", "Visual Studio" });

            EditorGUILayout.Space(8);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            switch (_selectedTab)
            {
                case EditorTab.VSCode:
                    DrawVSCodeTab(hasBinary);
                    break;
                case EditorTab.Rider:
                    DrawRiderTab(hasBinary);
                    break;
                case EditorTab.VisualStudio:
                    DrawVisualStudioTab(hasBinary);
                    break;
            }

            EditorGUILayout.EndScrollView();

            // Build output
            if (!string.IsNullOrEmpty(_buildOutput))
            {
                EditorGUILayout.Space(4);
                DrawSeparator();
                EditorGUILayout.LabelField("Build output:", EditorStyles.miniLabel);
                EditorGUILayout.SelectableLabel(_buildOutput, EditorStyles.miniLabel,
                    GUILayout.Height(40));
            }
        }

        private void DrawVSCodeTab(bool hasBinary)
        {
            EditorGUILayout.LabelField("VS Code Setup", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // Step 1: Build (already handled above)
            DrawStepLabel("1. Build LSP Server", hasBinary);

            // Step 2: Install extension
            bool hasExtension = false;
            string packageRoot = GetPackageRoot();
            string vsixDir = packageRoot != null
                ? Path.Combine(packageRoot, "editors~", "vscode")
                : null;

            DrawStepLabel("2. Install VS Code Extension", hasExtension);
            EditorGUI.BeginDisabledGroup(vsixDir == null || !Directory.Exists(vsixDir));
            if (GUILayout.Button("Install VS Code Extension", GUILayout.Height(24)))
            {
                InstallVSCodeExtension(vsixDir);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(4);

            // Step 3: Open VS Code
            DrawStepLabel("3. Open Project in VS Code", false);
            if (GUILayout.Button("Open VS Code", GUILayout.Height(24)))
            {
                string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                try
                {
                    Process.Start("code", $"\"{projectPath}\"");
                }
                catch (Exception ex)
                {
                    EditorUtility.DisplayDialog("Error",
                        $"Could not launch VS Code. Make sure 'code' is in your PATH.\n\n{ex.Message}",
                        "OK");
                }
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "After installing, set nori.lsp.path and nori.catalog.path in VS Code settings " +
                "if they are not automatically detected.",
                MessageType.Info);
        }

        private void DrawRiderTab(bool hasBinary)
        {
            EditorGUILayout.LabelField("JetBrains Rider Setup", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            DrawStepLabel("1. Build LSP Server", hasBinary);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("2. Configure Rider's LSP Client", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "In Rider, go to Settings > Languages & Frameworks > Language Servers:",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("  \u2022 Name: Nori", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  \u2022 Server executable: <path to nori-lsp>", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("  \u2022 File patterns: *.nori", EditorStyles.miniLabel);

            string catalogPath = NoriSettings.instance.ExternCatalogPath;
            if (!string.IsNullOrEmpty(catalogPath))
                EditorGUILayout.LabelField($"  \u2022 Arguments: --catalog \"{catalogPath}\"",
                    EditorStyles.miniLabel);
            else
                EditorGUILayout.LabelField("  \u2022 Arguments: (generate catalog first for full API)",
                    EditorStyles.miniLabel);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("3. Add TextMate Grammar", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Install the TextMate Bundles plugin, then add the editors~/vscode/ directory " +
                "as a TextMate bundle for syntax highlighting.",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(8);
            if (GUILayout.Button("Open Documentation", GUILayout.Height(24)))
            {
                Application.OpenURL("https://nori-lang.dev/editors/rider/");
            }
        }

        private void DrawVisualStudioTab(bool hasBinary)
        {
            EditorGUILayout.LabelField("Visual Studio Setup", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            DrawStepLabel("1. Build LSP Server", hasBinary);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("2. Configure LSP Client", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Create a .noriconfig file in your solution root with the LSP server path.",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("3. Register .nori File Type", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "In Visual Studio: Tools > Options > Text Editor > File Extension, " +
                "add .nori with Source Code (Text) Editor.",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "Visual Studio 2022 version 17.8 or later is required for LSP client support.",
                MessageType.Info);

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Open Documentation", GUILayout.Height(24)))
            {
                Application.OpenURL("https://nori-lang.dev/editors/visual-studio/");
            }
        }

        private void BuildLspServer()
        {
            string packageRoot = GetPackageRoot();
            if (packageRoot == null)
            {
                EditorUtility.DisplayDialog("Error",
                    "Could not locate the Nori package root directory.", "OK");
                return;
            }

            string toolsDir = Path.Combine(packageRoot, "tools~");
            string csproj = Path.Combine(toolsDir, "Nori.Lsp", "Nori.Lsp.csproj");
            if (!File.Exists(csproj))
            {
                EditorUtility.DisplayDialog("Error",
                    $"LSP project not found at:\n{csproj}", "OK");
                return;
            }

            string rid = GetRuntimeIdentifier();
            string binaryName = GetExpectedBinaryName();
            string publishDir = Path.Combine(toolsDir, "Nori.Lsp", "bin", "Release",
                "net8.0", rid, "publish");

            _isBuilding = true;
            _buildOutput = "";

            EditorUtility.DisplayProgressBar("Nori", "Building LSP server...", 0.3f);

            try
            {
                var psi = new ProcessStartInfo("dotnet",
                    $"publish \"{csproj}\" -r {rid} -c Release")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = toolsDir
                };

                var proc = Process.Start(psi);
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                EditorUtility.DisplayProgressBar("Nori", "Building LSP server...", 0.9f);

                if (proc.ExitCode == 0)
                {
                    string binaryPath = Path.Combine(publishDir, binaryName);
                    if (File.Exists(binaryPath))
                    {
                        _lspBinaryPath = binaryPath;
                        EditorPrefs.SetString(LspBinaryPathPref, _lspBinaryPath);
                        _buildOutput = $"Build succeeded. Binary: {binaryPath}";
                        Debug.Log($"[Nori] LSP server built: {binaryPath}");
                    }
                    else
                    {
                        _buildOutput = $"Build reported success but binary not found at:\n{binaryPath}";
                        Debug.LogWarning($"[Nori] {_buildOutput}");
                    }
                }
                else
                {
                    _buildOutput = !string.IsNullOrEmpty(stderr) ? stderr : stdout;
                    Debug.LogError($"[Nori] LSP server build failed:\n{_buildOutput}");
                }
            }
            catch (Exception ex)
            {
                _buildOutput = ex.Message;
                Debug.LogError($"[Nori] Failed to start build: {ex.Message}");
            }
            finally
            {
                _isBuilding = false;
                EditorUtility.ClearProgressBar();
            }
        }

        private void InstallVSCodeExtension(string vsixDir)
        {
            // Look for an existing .vsix file
            string[] vsixFiles = Directory.GetFiles(vsixDir, "*.vsix");
            string vsixPath = vsixFiles.Length > 0 ? vsixFiles[0] : null;

            if (vsixPath == null)
            {
                // Try to package the extension
                EditorUtility.DisplayProgressBar("Nori", "Packaging VS Code extension...", 0.3f);
                try
                {
                    var psi = new ProcessStartInfo("npm", "run package")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = vsixDir
                    };
                    var proc = Process.Start(psi);
                    proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();

                    if (proc.ExitCode != 0)
                    {
                        EditorUtility.ClearProgressBar();
                        EditorUtility.DisplayDialog("Error",
                            $"Failed to package VS Code extension.\n\n" +
                            "Make sure Node.js and npm are installed, then run:\n" +
                            $"  cd \"{vsixDir}\"\n  npm install\n  npm run package\n\n{stderr}",
                            "OK");
                        return;
                    }

                    vsixFiles = Directory.GetFiles(vsixDir, "*.vsix");
                    vsixPath = vsixFiles.Length > 0 ? vsixFiles[0] : null;
                }
                catch (Exception ex)
                {
                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog("Error",
                        $"Could not run npm. Make sure Node.js is installed.\n\n{ex.Message}",
                        "OK");
                    return;
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }
            }

            if (vsixPath == null)
            {
                EditorUtility.DisplayDialog("Error",
                    "No .vsix file found after packaging. Check the VS Code extension directory.",
                    "OK");
                return;
            }

            // Install via code CLI
            EditorUtility.DisplayProgressBar("Nori", "Installing VS Code extension...", 0.7f);
            try
            {
                var psi = new ProcessStartInfo("code",
                    $"--install-extension \"{vsixPath}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var proc = Process.Start(psi);
                string stdout = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                if (proc.ExitCode == 0)
                {
                    Debug.Log($"[Nori] VS Code extension installed from {vsixPath}");
                    EditorUtility.DisplayDialog("Success",
                        "Nori VS Code extension installed successfully.", "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Error",
                        "Failed to install the extension. Make sure 'code' is in your PATH.\n\n" +
                        $"You can install manually:\n  code --install-extension \"{vsixPath}\"",
                        "OK");
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error",
                    $"Could not launch VS Code CLI.\n\n{ex.Message}\n\n" +
                    $"Install manually:\n  code --install-extension \"{vsixPath}\"",
                    "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void DrawStepLabel(string label, bool completed)
        {
            EditorGUILayout.BeginHorizontal();
            string icon = completed ? "\u2714" : "\u25cb";
            EditorGUILayout.LabelField($"{icon}  {label}", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawSeparator()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
        }
    }
}
#endif
