
using System.Net;

namespace Matter.Core
{
    public class BLEEndPoint : EndPoint
    {
        private string address;

        public BLEEndPoint(string address)
        {
            this.address = address;
        }

        public string Address { get { return address; } }
    }
}
