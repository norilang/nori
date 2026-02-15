using System;
using System.Threading.Tasks;
using StreamJsonRpc;
using Nori.Lsp.Server;

namespace Nori.Lsp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string catalogPath = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--catalog" && i + 1 < args.Length)
                {
                    catalogPath = args[++i];
                }
            }

            var server = new NoriLanguageServer(catalogPath);

            // Redirect stderr for logging (stdout is used by JSON-RPC)
            server.Log("Nori LSP server starting...");

            using var stdIn = Console.OpenStandardInput();
            using var stdOut = Console.OpenStandardOutput();

            var handler = new HeaderDelimitedMessageHandler(stdOut, stdIn);
            using var rpc = new JsonRpc(handler);
            rpc.AddLocalRpcTarget(server);

            server.SetRpc(rpc);

            rpc.StartListening();

            server.Log("Nori LSP server listening on stdio.");

            await server.WaitForShutdown();
        }
    }
}
