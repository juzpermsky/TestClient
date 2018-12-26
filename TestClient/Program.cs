using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using Network;

namespace TestClient
{
    public class Client
    {
        public string name;
        private IPEndPoint serverEndpoint;
        public int serverId = -1;
        public NewNet net;
        private byte[] sendSample;
        private bool receiving;
        private bool sending;
        private Thread sendingThread;

        public Client(string serverIP, int serverPort, string name = null)
        {
            this.name = name;
            net = new NewNet(100, new IPEndPoint(IPAddress.Parse("192.168.1.100"), 5001), name);
            serverEndpoint = new IPEndPoint(IPAddress.Parse(serverIP), serverPort);
            sendSample = new byte[100];
        }

        public void Start()
        {
            net.Listen();
            receiving = true;
            var th = new Thread(Receiving);
            th.Start();

            net.Connect(serverEndpoint.Address, serverEndpoint.Port);
        }

        public void Sending()
        {
            var i = 0;
            while (sending&&i<10000)
            {
                if (serverId >= 0)
                {
                    net.Send(serverId, sendSample, false);
                    i++;
                }
            }
        }


        public void Receiving()
        {
            while (receiving)
            {
                var netEvent = net.Receive();
                if (netEvent != null)
                {
                    switch (netEvent.type)
                    {
                        case NetEventType.Connected:
                            string ip;
                            int port;
                            string err;
                            net.GetConnectionInfo(netEvent.connId, out ip, out port, out err);
                            if (new IPEndPoint(IPAddress.Parse(ip), port).Equals(serverEndpoint))
                            {
                                serverId = netEvent.connId;
                                if (sendingThread == null)
                                {
                                    sendingThread = new Thread(Sending);
                                    sending = true;
                                    sendingThread.Start();
                                }
                            }

                            break;
                        case NetEventType.Data:
                            break;
                    }
                }

            }
        }
    }

    public class Server
    {
        public string name;
        private bool receiving;
        public List<int> connections = new List<int>();
        public NewNet net;

        public Server(string ip, int port, string name = null)
        {
            this.name = name;
            net = new NewNet(100, new IPEndPoint(IPAddress.Parse(ip), port), name);
        }

        public void Start()
        {
            net.Listen();
            receiving = true;
            var th = new Thread(Receiving);
            th.Start();
        }

        public void Receiving()
        {
            var t1 = DateTime.Now;
            while (receiving)
            {
                var netEvent = net.Receive();
                if (netEvent != null)
                {
                    switch (netEvent.type)
                    {
                        case NetEventType.Connected:
                            string ip;
                            int port;
                            string err;
                            net.GetConnectionInfo(netEvent.connId, out ip, out port, out err);
                            break;
                        case NetEventType.Data:
                            if (!netEvent.reliable && netEvent.sequenceId % 1000 == 0)
                            {
                                Console.WriteLine(
                                    $"{netEvent.sequenceId} unreliable messages received at {DateTime.Now - t1}");
                            }

                            break;
                    }
                }
            }
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var server = new Server("192.168.1.100", 5000, "Server");
            var client1 = new Client("192.168.1.100", 5000, "Client1");

            server.Start();

            client1.Start();
        }
    }
}