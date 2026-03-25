
namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua.Configuration;
    using Serilog;
    using System;
    using System.Threading.Tasks;

    public class ApplicationMessageDlg : IApplicationMessageDlg
    {
        private string message = string.Empty;
        private bool ask = false;

        public override void Message(string text, bool ask)
        {
            this.message = text;
            this.ask = ask;
        }

        public override async Task<bool> ShowAsync()
        {
            if (ask)
            {
                message += " (y/n, default y): ";
                Log.Logger.Information(message);
            }
            else
            {
                Log.Logger.Information(message);
            }

            if (ask)
            {
                try
                {
                    ConsoleKeyInfo result = Console.ReadKey();
                    return await Task.FromResult((result.KeyChar == 'y') || (result.KeyChar == 'Y') || (result.KeyChar == '\r')).ConfigureAwait(false);
                }
                catch
                {
                    // intentionally fall through
                }
            }

            // always return yes by default
            return await Task.FromResult(true).ConfigureAwait(false);
        }
    }
}
