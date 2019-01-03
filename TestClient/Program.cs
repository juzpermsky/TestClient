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
            while (sending && i < 10000)
            {
                if (serverId >= 0)
                {
                    net.Send(serverId, sendSample, false);
                    i++;
                    Thread.Sleep(1);
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
                            Console.WriteLine($"Connected to server {ip}:{port}");
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

    class Program
    {
        static void Main(string[] args)
        {
            var client1 = new Client("138.68.99.154", 5000, "Client1");
//            var client1 = new Client("192.168.1.100", 5000, "Client1");

            client1.Start();
        }
    }
}