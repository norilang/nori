import * as path from "path";
import * as fs from "fs";
import {
  workspace,
  ExtensionContext,
  window,
} from "vscode";
import {
  LanguageClient,
  LanguageClientOptions,
  ServerOptions,
} from "vscode-languageclient/node";

let client: LanguageClient | undefined;

export function activate(context: ExtensionContext): void {
  const serverPath = resolveServerPath(context);

  if (!serverPath) {
    window.showInformationMessage(
      "Nori language server not found. Syntax highlighting and snippets are " +
      "still active. To enable diagnostics and completions, set \"nori.lsp.path\" " +
      "in your settings or place the server binary in the extension's server/ folder."
    );
    return;
  }

  const serverArgs: string[] = [];

  const catalogPath = workspace
    .getConfiguration("nori")
    .get<string>("catalog.path", "");

  if (catalogPath) {
    serverArgs.push("--catalog", catalogPath);
  }

  const serverOptions: ServerOptions = {
    command: serverPath,
    args: serverArgs,
    options: {
      env: process.env,
    },
  };

  const clientOptions: LanguageClientOptions = {
    documentSelector: [{ scheme: "file", language: "nori" }],
    synchronize: {
      fileEvents: workspace.createFileSystemWatcher("**/*.nori"),
    },
  };

  client = new LanguageClient(
    "nori",
    "Nori Language Server",
    serverOptions,
    clientOptions
  );

  client.start();
}

export function deactivate(): Thenable<void> | undefined {
  if (!client) {
    return undefined;
  }
  return client.stop();
}

function resolveServerPath(context: ExtensionContext): string | undefined {
  // 1. Check user-configured path first.
  const configuredPath = workspace
    .getConfiguration("nori")
    .get<string>("lsp.path", "");

  if (configuredPath) {
    const resolved = resolveAbsoluteOrWorkspace(configuredPath);
    if (resolved && fs.existsSync(resolved)) {
      return resolved;
    }
    window.showWarningMessage(
      `Nori LSP path "${configuredPath}" does not exist. Falling back to bundled server.`
    );
  }

  // 2. Look for the bundled server binary inside the extension.
  const ext = process.platform === "win32" ? ".exe" : "";
  const bundledPath = path.join(
    context.extensionPath,
    "server",
    `nori-lsp${ext}`
  );

  if (fs.existsSync(bundledPath)) {
    return bundledPath;
  }

  return undefined;
}

function resolveAbsoluteOrWorkspace(p: string): string | undefined {
  if (path.isAbsolute(p)) {
    return p;
  }

  const folders = workspace.workspaceFolders;
  if (folders && folders.length > 0) {
    return path.join(folders[0].uri.fsPath, p);
  }

  return undefined;
}
