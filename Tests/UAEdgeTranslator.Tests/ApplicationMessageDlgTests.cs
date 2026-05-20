namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua.Configuration;
    using System.Threading.Tasks;
    using Xunit;

    public class ApplicationMessageDlgTests
    {
        [Fact]
        public async Task ShowAsync_returns_false_for_ask_messages()
        {
            // Headless server contract: any interactive SDK prompt must be
            // refused so the explicit certificate validation callbacks own
            // the trust decision.
            ApplicationMessageDlg dlg = new();
            dlg.Message("create cert?", ask: true);

            bool result = await dlg.ShowAsync().ConfigureAwait(false);

            Assert.False(result);
        }

        [Fact]
        public async Task ShowAsync_returns_false_for_informational_messages()
        {
            ApplicationMessageDlg dlg = new();
            dlg.Message("hello", ask: false);

            bool result = await dlg.ShowAsync().ConfigureAwait(false);

            Assert.False(result);
        }

        [Fact]
        public void Dialog_implements_SDK_contract()
        {
            ApplicationMessageDlg dlg = new();
            Assert.IsAssignableFrom<IApplicationMessageDlg>(dlg);
        }
    }
}
