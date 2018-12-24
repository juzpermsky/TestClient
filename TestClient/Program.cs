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
            net = new NewNet(100, new IPEndPoint(IPAddress.Parse("192.168.1.100"), 0), name);
            serverEndpoint = new IPEndPoint(IPAddress.Parse(serverIP), serverPort);
            sendSample = new byte[10];
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
            while (sending)
            {
                if (serverId >= 0)
                {
                    net.Log.Info("sending message, {0}", DateTime.Now.ToString("hh.mm.ss.ffffff"));
                    net.Send(serverId, sendSample, true);
                }
                Thread.Sleep(1);
            }
        }


        public void Receiving()
        {
            while (receiving)
            {
                var netEvent = net.Receive();

                switch (netEvent.type)
                {
                    case NetEventType.Connected:
                        string ip;
                        int port;
                        string err;
                        net.GetConnectionInfo(netEvent.connId, out ip, out port, out err);
                        if (new IPEndPoint(IPAddress.Parse(ip), port).Equals(serverEndpoint))
                        {
                            net.Log.Info("Connected to server, connId {0}", netEvent.connId);
                            serverId = netEvent.connId;
                            if (sendingThread == null)
                            {
                                sendingThread = new Thread(Sending);
                                sending = true;
                                sendingThread.Start();
                            }
                            else
                            {
                                net.Log.Info("Sending thread already started");
                            }
                        }
                        else
                        {
                            net.Log.Info("Connected {0}:{1}, connId {2}", ip, port, netEvent.connId);
                        }

                        break;
                    case NetEventType.Data:
                        net.Log.Info("Data received from connId {0}, {1} bytes", netEvent.connId,
                            netEvent.data.Length);
                        break;
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
            while (receiving)
            {
                var netEvent = net.Receive();
                switch (netEvent.type)
                {
                    case NetEventType.Connected:
                        string ip;
                        int port;
                        string err;
                        net.GetConnectionInfo(netEvent.connId, out ip, out port, out err);
                        net.Log.Info("Connected {0}:{1}, connId {2}", ip, port, netEvent.connId);
                        break;
                }
            }
        }
    }

    class Program
    {
        static void Logging()
        {
            while (true)
            {
                Logs.WriteMultithreadedLogs();
            }
        }

        static void Main(string[] args)
        {
            var th = new Thread(Logging);
            th.Start();
            var server = new Server("192.168.1.100", 5000, "Server");
            var client1 = new Client("192.168.1.100", 5000, "Client1");

            server.Start();

            client1.Start();
            

            
        }
    }
}