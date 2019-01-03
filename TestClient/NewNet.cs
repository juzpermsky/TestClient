using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Network
{
    public enum NetMessage : byte
    {
        ConnectRequest,
        ConnectAccept,
        Payload,
        Disconnect,
        ConnectDenied
    }

    public enum QOS : byte
    {
        Unreliable,
        Reliable,
        Ack
    }

    internal class ReliableReceiver
    {
        public Connection connection;
        public RawRcvMsg[] queue = new RawRcvMsg[NewNet.SeqBufSize];
        public uint[] seqBuf = new uint[NewNet.SeqBufSize];
        public ushort expectedSeqId = 1;
        public ushort recentSeqId;
        public ushort processedSeqId;
        private bool work;


        public ReliableReceiver(Connection connection)
        {
            this.connection = connection;
            // Готовим цикличный буфер
            SeqBufReset();
        }

        public void Start()
        {
            work = true;
            var workerThread = new Thread(Worker);
            workerThread.Start();
        }

        public void Stop()
        {
            work = false;
        }

        public string Receive(BinaryReader br)
        {
            var msgNum = br.ReadByte();
            var s = "";
            while (msgNum > 0)
            {
                var seqId = br.ReadUInt16();
                s += seqId + ",";
                // Размер сообщения
                var msgBytes = br.ReadUInt16();
                // Тело сообщения
                var msgData = br.ReadBytes(msgBytes);
                var err = ReceiveMessage(seqId, msgData);
                if (err != null)
                {
                    return err;
                }

                msgNum--;
            }


//			Debug.Log(DateTime.Now.ToString("hh.mm.ss.ffffff") + ":received seqs: " + s);

            // Разобрали все сообщения, из reliable пакета. Теперь можно составить ack пакет
            var ack = recentSeqId;
            var ackBits = CreateAckBits(ack);

            using (var ms = new MemoryStream(new byte[8], 0, 8, true, true))
            using (var bw = new BinaryWriter(ms))
            {
                ms.WriteByte((byte) NetMessage.Payload);
                ms.WriteByte((byte) QOS.Ack);
                bw.Write(ack);
                bw.Write(ackBits);

//				Debug.Log(DateTime.Now.ToString("hh.mm.ss.ffffff") + ":sending ack: " + ack);
                connection.Send(ms.GetBuffer(), "ack: " + ack);
            }

            return null;
        }

        private string ReceiveMessage(ushort seqId, byte[] data)
        {
            Console.WriteLine($"{seqId} message received");
            if (GetRawMsg(seqId) != null)
            {
                // Повторное получение сообщения - скипаем
                Console.WriteLine($"duplicated seqId ({seqId}) received - skipped");
                return null;
            }

            return InsertRawMsg(seqId, new RawRcvMsg
            {
                sequenceId = seqId,
                data = data
            });
        }

        private uint CreateAckBits(ushort ack)
        {
            uint mask = 1;
            uint ackBits = 0;
            for (var seqId = (ushort) (ack - 1); !SeqLessThan(seqId, (ushort) (ack - 32)); seqId--)
            {
                if (seqBuf[seqId % NewNet.SeqBufSize] == seqId || SeqLessThan(seqId, expectedSeqId))
                {
                    ackBits |= mask;
                }

                mask <<= 1;
            }

            return ackBits;
        }


        private RawRcvMsg GetRawMsg(ushort seqId)
        {
            var index = seqId % NewNet.SeqBufSize;
            if (seqBuf[index] == seqId)
            {
                return queue[index];
            }

            return null;
        }

        private string InsertRawMsg(ushort seqId, RawRcvMsg rawRcvMsg)
        {
            if (SeqLessThan(seqId, (ushort) (recentSeqId - NewNet.SeqBufSize)))
            {
                return "cannot insert in SequenceBuffer: sequenceId is too old";
            }

            if (SeqGreaterThan(seqId, (ushort) (recentSeqId + 1)))
            {
                SeqBufClean((ushort) (recentSeqId + 1), (ushort) (seqId - 1));
            }

            if (SeqGreaterThan(seqId, recentSeqId))
            {
                recentSeqId = seqId;
            }

//			Debug.LogFormat("inserting in reliable queue seqId ({0})", seqId);
            var index = seqId % NewNet.SeqBufSize;
            seqBuf[index] = seqId;
            queue[index] = rawRcvMsg;
            if (expectedSeqId == seqId)
            {
                expectedSeqId++;
//				Debug.LogFormat("expectedSeqId: {0}", expectedSeqId);
            }

            return null;
        }

        private static bool SeqGreaterThan(ushort seqId, ushort other)
        {
            return seqId > other && seqId - other <= 32768 || seqId < other && other - seqId > 32768;
        }

        private static bool SeqLessThan(ushort seqId, ushort other)
        {
            return SeqGreaterThan(other, seqId);
        }

        private void SeqBufClean(ushort from, ushort to)
        {
            var from32 = (uint) from;
            var to32 = (uint) to;
            if (to32 < from32)
            {
                to32 += 65536;
            }

            if (to32 - from32 < NewNet.SeqBufSize)
            {
                for (var seqId = from32; seqId <= to32; seqId++)
                {
                    seqBuf[seqId % NewNet.SeqBufSize] = 0xffffffff;
                }
            }
            else
            {
                SeqBufReset();
            }
        }

        private void SeqBufReset()
        {
            if (seqBuf.Length == 0)
            {
                return;
            }

            seqBuf[0] = 0xffffffff;
            for (var bp = 1; bp < seqBuf.Length; bp *= 2)
            {
                Array.Copy(seqBuf, 0, seqBuf, bp, Math.Min(seqBuf.Length - bp, bp));
            }
        }

        private void Worker()
        {
            while (work)
            {
                if (SeqLessThan(processedSeqId, (ushort) (expectedSeqId - 1)))
                {
                    // Есть reliable сообщения, не переданные в обработку
                    var rawRcvMsg = GetRawMsg((ushort) (processedSeqId + 1));
                    if (rawRcvMsg != null)
                    {
                        lock (connection.newNet.eventQueue)
                        {
                            connection.newNet.eventQueue.Enqueue(new NetEvent
                            {
                                type = NetEventType.Data,
                                data = rawRcvMsg.data,
                                sequenceId = rawRcvMsg.sequenceId,
                                reliable = true,
                                connId = connection.id
                            });
                        }

                        processedSeqId++;
                    }
                    else
                    {
                        System.Console.WriteLine("Cannot find seqId ({0})\n", processedSeqId + 1);
                    }
                }
            }
        }
    }

    internal class RawRcvMsg
    {
        public ushort sequenceId;
        public byte[] data;
    }

    internal class RawSndMsg
    {
        public bool sent;
        public ushort sequenceId;
        public DateTime firstSendTime;
        public DateTime lastSendTime;
        public byte[] data;
    }

    internal class ReliableSender
    {
        public Connection connection;
        public ushort seqId = 1;
        public uint[] seqBuf = new uint[NewNet.SeqBufSize];
        public RawSndMsg[] queue = new RawSndMsg[NewNet.SeqBufSize];
        private bool work;

        public ReliableSender(Connection connection)
        {
            this.connection = connection;
            // Готовим цикличный буфер
            SeqBufReset();
        }

        public void Start()
        {
            work = true;
            var workerThread = new Thread(Worker);
            workerThread.Start();
        }

        public void Stop()
        {
            work = false;
        }

        public RawSndMsg GetRawMsg(ushort seqId)
        {
            var index = seqId % NewNet.SeqBufSize;
            if (seqBuf[index] == seqId)
            {
                return queue[index];
            }

            return null;
        }

        public ushort InsertRawMsg(RawSndMsg rawSndMsg)
        {
            rawSndMsg.sequenceId = seqId;
            var index = seqId % NewNet.SeqBufSize;
            seqBuf[index] = seqId;
            queue[index] = rawSndMsg;
            if (seqId % 1000 == 0)
            {
                Console.WriteLine($"{seqId} inserted");
            }

            seqId++;
            return seqId;
        }

        private void SeqBufReset()
        {
            if (seqBuf.Length == 0)
            {
                return;
            }

            seqBuf[0] = 0xffffffff;
            for (var bp = 1; bp < seqBuf.Length; bp *= 2)
            {
                Array.Copy(seqBuf, 0, seqBuf, bp, Math.Min(seqBuf.Length - bp, bp));
            }
        }

        public void ReceiveAck(BinaryReader br)
        {
            var ack = br.ReadUInt16();
            //string sacks = "" + ack;
            var ackBits = br.ReadUInt32();
            Console.WriteLine($"{DateTime.Now.ToString("hh.mm.ss.ffffff")} :received ack: {ack}:{ackBits}");

            var rawSndMsg = GetRawMsg(ack);
            if (rawSndMsg != null)
            {
                connection.UpdateRTT(DateTime.Now - rawSndMsg.firstSendTime);
                var index = ack % NewNet.SeqBufSize;
                // Удаляем данные из буфера и очереди
                seqBuf[index] = 0xffffffff;
                queue[index] = null;
            }

            for (var i = 1; i <= 32; i++)
            {
                if ((ackBits & 1) == 1)
                {
                    var seqId = ack - i;
                    //sacks += "," + seqId;
                    rawSndMsg = GetRawMsg((ushort) seqId);
                    if (rawSndMsg != null)
                    {
                        var index = seqId % NewNet.SeqBufSize;
                        // Удаляем данные из буфера и очереди
                        seqBuf[index] = 0xffffffff;
                        queue[index] = null;
                    }
                }

                ackBits >>= 1;
            }

//			Debug.LogFormat(DateTime.Now.ToString("hh.mm.ss.ffffff") + ":received ack: {0}:{1}", ack, ackBits);
        }

        public void Worker()
        {
            // Сбор reliable unacked сообщений в пачку и отправка
            var sendBytes = 0;
            byte msgNum = 0;
            string seqs = "";
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
                while (work)
                {
                    ms.SetLength(0);
                    bw.Write((byte) NetMessage.Payload);
                    bw.Write((byte) QOS.Reliable);
                    bw.Write((byte) 0); // Под число сообщений
                    for (var index = 0; index < NewNet.SeqBufSize; index++)
                    {
                        var seqId = seqBuf[index];
                        if (seqId != 0xffffffff)
                        {
                            var rawSndMsg = GetRawMsg((ushort) seqId);
                            if (rawSndMsg != null)
                            {
                                if (sendBytes + rawSndMsg.data.Length + 4 > NewNet.MaxDataSize || msgNum == 255)
                                {
                                    // Сообщение не влезет в буфер - делаем отправку, того что накопилось, и начинаем собирать новый буфер.
                                    var packetData = ms.ToArray();
                                    packetData[2] = msgNum;

//									Debug.LogFormat("filfull sending {0}", packetData.Length);
//									Debug.LogFormat("msgnum: {0}, sendBytes: {1}, rawSndMsg.data.length: {2}, rawSndMsg.seqienceId: {3}", msgNum, sendBytes, rawSndMsg.data.Length, rawSndMsg.sequenceId);
//									Debug.LogFormat("seqs: {0}", seqs);
                                    connection.Send(packetData, "seqs: " + seqs);
                                    seqs = "";
                                    sendBytes = 0;
                                    msgNum = 0;
                                    ms.SetLength(0);
                                    bw.Write((byte) NetMessage.Payload);
                                    bw.Write((byte) QOS.Reliable);
                                    bw.Write((byte) 0); // Под число сообщений
                                }

                                // Проверим была ли отправка и когда отправляли последний раз
                                if (!rawSndMsg.sent || DateTime.Now - rawSndMsg.lastSendTime > connection.rtt)
                                {
                                    // Надо переотправить сообщение
                                    if (!rawSndMsg.sent)
                                    {
                                        // Еще не отправлялось
                                        rawSndMsg.firstSendTime = DateTime.Now;
                                        rawSndMsg.sent = true;
                                    }
                                    else
                                    {
                                        Console.WriteLine($"resending seqId {rawSndMsg.sequenceId}");
                                    }

                                    rawSndMsg.lastSendTime = DateTime.Now;
                                    bw.Write((ushort) seqId);
                                    bw.Write((ushort) rawSndMsg.data.Length);
                                    bw.Write(rawSndMsg.data);
                                    seqs += seqId + ",";
                                    sendBytes += rawSndMsg.data.Length + 4;
                                    msgNum++;
                                }
                            }
                        }
                    }

//                    connection.newNet.Log.Info("end of cycle");

                    // После завершения прохождения цикличного буфера еще раз отправляем если что-то накопилось
                    if (sendBytes > 0)
                    {
                        var packetData = ms.ToArray();
                        packetData[2] = msgNum;
//						Debug.LogFormat("end sending {0}", packetData.Length);
//						Debug.LogFormat("msgnum: {0}, sendBytes: {1}", msgNum, sendBytes);
//						Debug.LogFormat(DateTime.Now.ToString("hh.mm.ss.ffffff") + ":sending seqs: {0}", seqs);
//						Debug.LogFormat("reliable send");
                        connection.Send(packetData, "seqs: " + seqs);
                        seqs = "";
                        sendBytes = 0;
                        msgNum = 0;
                    }
                }
        }
    }

    internal class Connection
    {
        internal int id;
        internal int remoteId;
        public bool accepted;
        internal int tryConnectNum;
        public NewNet newNet;
        public IPEndPoint endPoint;
        public ReliableReceiver reliableReceiver;
        public ReliableSender reliableSender;
        internal TimeSpan rtt;
        private ushort uSeqId;

        public Connection(int id, IPEndPoint endPoint, bool accepted, NewNet newNet)
        {
            this.id = id;
            this.accepted = accepted;
            this.newNet = newNet;
            this.endPoint = endPoint;
            reliableSender = new ReliableSender(this);
            reliableReceiver = new ReliableReceiver(this);
            rtt = TimeSpan.FromMilliseconds(100);
            tryConnectNum = 0;
            uSeqId = 0;
        }

        public void ReceiveUnreliable(BinaryReader br)
        {
            var sequenceId = br.ReadUInt16();
            //Console.WriteLine(sequenceId);
            var data = br.ReadBytes((int) (br.BaseStream.Length - br.BaseStream.Position));
            // Закидываем в общую очередь всего полученного
            lock (newNet.eventQueue)
            {
                newNet.eventQueue.Enqueue(new NetEvent
                {
                    type = NetEventType.Data,
                    data = data,
                    sequenceId = sequenceId,
                    reliable = false,
                    connId = id
                });
            }
        }

        public void Send(byte[] data, string nums = null)
        {
            lock (newNet.sendQueueLock)
            {
                newNet.sendQueue.Enqueue(new SendItem
                {
                    data = data,
                    endPoint = endPoint,
                    nums = nums
                });
            }
        }

        internal void SendUnreliable(byte[] data)
        {
            var sendBuf = new byte[data.Length + 4];
            using (var ms = new MemoryStream(sendBuf, 0, data.Length + 4, true, true))
            using (var bw = new BinaryWriter(ms))
            {
                ms.WriteByte((byte) NetMessage.Payload);
                ms.WriteByte((byte) QOS.Unreliable);
                bw.Write(uSeqId);
                bw.Write(data);
                uSeqId++;
            }

            Send(sendBuf);
        }

        public void UpdateRTT(TimeSpan rtt)
        {
            //todo: оставляю пока равным 0,1 всегда
            this.rtt = new TimeSpan(
                (long) (NewNet.SmoothFactor * rtt.Ticks + (1 - NewNet.SmoothFactor) * this.rtt.Ticks));
            Console.WriteLine($"rtt: {this.rtt}");
        }
    }


    public enum NetEventType
    {
        Connected,
        Data,
        Disconnected,
        Nothing
    }

    public class NetEvent
    {
        public NetEventType type;
        public byte[] data;
        public int connId;
        public ushort sequenceId;
        public bool reliable;
    }

    public class SendItem
    {
        public IPEndPoint endPoint;
        public byte[] data;
        public string nums;
    }


    public class NewNet
    {
        public const int SeqBufSize = 1024;
        public const int MaxDataSize = 1000;
        public const int TryConnectLimit = 10;
        public const float SmoothFactor = 0.1f;
        private bool listening;
        private bool sending;
        private byte[] inBuffer = new byte[65507];
        private byte[] outBuffer = new byte[65507];
        internal Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        private int maxConnections;
        private int numConnections;
        private Connection[] connections;
        private bool[] usedConnections;
        internal Queue<NetEvent> eventQueue = new Queue<NetEvent>();
        internal Queue<SendItem> sendQueue = new Queue<SendItem>();
        private static object socketLock = new object();
        public object sendQueueLock = new object();

        public NewNet(int maxConnections, IPEndPoint endPoint = null, string sourceName = null)
        {
            this.maxConnections = maxConnections;
            numConnections = 0;
            usedConnections = new bool[maxConnections];
            connections = new Connection[maxConnections];
            // Нужно сделать привязку localEndPoint перед .ReceiveFrom - доверяем провайдеру с выбором
            if (endPoint == null)
            {
                endPoint = new IPEndPoint(IPAddress.Any, 0);
            }

            socket.Bind(endPoint);
        }

        /// <summary>
        /// Отправляет ConnectRequest по указанному адресу 
        /// </summary>
        public int Connect(IPAddress ip, int port)
        {
            // Занимаем свободный Connection слот
            var connId = -1;
            for (var i = 0; i < maxConnections; i++)
            {
                if (!usedConnections[i])
                {
                    // Найден свободный слот
                    connId = i;
                    var endPoint = new IPEndPoint(ip, port);
                    usedConnections[i] = true;
                    connections[i] = new Connection(connId, endPoint, false, this);
                    numConnections++;
                    // В отдельном потоке делаем попытки коннекта несколько раз
                    var connThread = new Thread(Connecting);
                    connThread.Start(connections[i]);
                    break;
                }
            }

            // Возврат connId еще не гарантирует установку соединения, только сообщает выделенный слот.
            // Надо мониторить состояние соединения (Connected) в NetEvent
            return connId;
        }

        private void Connecting(object connObj)
        {
            var connection = (Connection) connObj;
            var data = new[] {(byte) NetMessage.ConnectRequest};
            while (!connection.accepted && connection.tryConnectNum < TryConnectLimit)
            {
                outBuffer[0] = (byte) NetMessage.ConnectRequest;
                lock (socketLock)
                {
                    socket.SendTo(data, 1, SocketFlags.None, connection.endPoint);
                }

                Thread.Sleep(connection.rtt);
                connection.tryConnectNum++;
            }

            if (!connection.accepted && connection.tryConnectNum == TryConnectLimit)
            {
                // Все попытки подключиться исчерпаны, делаем Disconnect 
                ProcessDisconnect(connection.endPoint);
            }
        }

        public int GetLocalPort()
        {
            return ((IPEndPoint) socket.LocalEndPoint).Port;
        }

        public void GetConnectionInfo(int connId, out string ip, out int port, out string error)
        {
            ip = null;
            port = 0;
            error = null;
            if (connId >= maxConnections)
            {
                error = "invalid connId";
                return;
            }

            if (!usedConnections[connId])
            {
                error = "unregistered connId";
                return;
            }

            var endPoint = connections[connId].endPoint;
            ip = endPoint.Address.ToString();
            port = endPoint.Port;
        }

        public string Disconnect(int connId)
        {
            if (connId >= maxConnections)
            {
                return "invalid connId";
            }

            if (!usedConnections[connId])
            {
                return "unregistered connId";
            }

            //todo: Может быть делать это в отдельном потоке?
            var connection = connections[connId];
            if (connection.accepted)
            {
                var data = new[] {(byte) NetMessage.Disconnect};
                for (var i = 1; i <= 10; i++)
                {
                    connection.Send(data);
                }

                // Останавливаем reliable workers
                connection.reliableSender.Stop();
                connection.reliableReceiver.Stop();
                // Освобождаем слот
                usedConnections[connId] = false;
                connections[connId] = null;
                numConnections--;
                // Закидываем в очередь событий отключение
                lock (eventQueue)
                {
                    eventQueue.Enqueue(new NetEvent
                    {
                        type = NetEventType.Disconnected,
                        connId = connId
                    });
                }
            }

            return null;
        }

        public void Listen()
        {
            listening = true;
            var listenThread = new Thread(Listening); //{IsBackground = true};
            listenThread.Start();
            // так как сокет в блокирующем режиме, нужен отдельный поток для отправки сообщений,
            // чтоб не блокировать приложение, складываем сообщения на отправку в отдельную очередь
            sending = true;
            var sendingThread = new Thread(Sending);
            sendingThread.Start();
        }

        private void Sending()
        {
            var t1 = DateTime.Now;
            var msgCount = 0;
            while (sending)
            {
                if (sendQueue.Count > 0)
                {
                    SendItem sendItem;
                    lock (sendQueueLock)
                    {
                        sendItem = sendQueue.Dequeue();
                    }

                    lock (socketLock)
                    {
                        socket.SendTo(sendItem.data, sendItem.endPoint);
                        msgCount++;
                        if (msgCount % 10 == 0)
                        {
                            var t2 = DateTime.Now;
                            Console.WriteLine($"{msgCount} datagrams sent after {t2 - t1}");
                        }
                    }
                }
            }
        }

        private void Listening()
        {
//			Debug.LogFormat("start listening {0}:{1}", ((IPEndPoint) socket.LocalEndPoint).Address, ((IPEndPoint) socket.LocalEndPoint).Port);
            using (var ms = new MemoryStream(inBuffer))
            using (var br = new BinaryReader(ms))
                while (listening)
                {
                    try
                    {
                        EndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);
                        // Убрал available, так как слушаем в отдельном потоке и сокет в режиме блокировки
                        // проверка available приводит к итерации цикла и загружает CPU, в то время как
                        // простой ReceiveFrom блокирует выполнение и не грузит CPU лишними проверками
                        //if (socket.Available > 0) {
//							Debug.LogFormat("available {0}", socket.Available);
                        int rcv;
                        rcv = socket.ReceiveFrom(inBuffer, ref endPoint);

//                        Log.Info("{0} bytes received from {1}", rcv, endPoint);
                        ms.Position = 0;
                        var netMsg = (NetMessage) br.ReadByte();
                        string err = null;
                        switch (netMsg)
                        {
                            case NetMessage.ConnectRequest:
//									Debug.LogFormat("NetMessage is {0}", netMsg);
                                if (numConnections < maxConnections)
                                {
                                    // Запрос на соединение
                                    ProcessConnectRequest((IPEndPoint) endPoint);
                                }
                                else
                                {
                                    // Все коннекты заняты => шлём отказ
                                    outBuffer[0] = (byte) NetMessage.ConnectDenied;
                                    lock (socketLock)
                                    {
                                        socket.SendTo(outBuffer, 1, SocketFlags.None, endPoint);
                                    }
                                }

                                break;
                            case NetMessage.Payload:
                                var payload = new byte[rcv - 1];
//                                var s = "";
//                                for (var i = 0; i < rcv; i++)
//                                {
//                                    s += " " + inBuffer[i];
//                                }
//									Debug.Log("payload received:" + s);


                                Array.Copy(inBuffer, 1, payload, 0, rcv - 1);
                                err = ReceivePayload(payload, (IPEndPoint) endPoint);
                                if (err != null)
                                {
                                    Console.WriteLine(err);
                                }

                                break;
                            case NetMessage.ConnectAccept:
                                ProcessConnectAccept((IPEndPoint) endPoint);
                                break;
                            case NetMessage.Disconnect:
                            case NetMessage.ConnectDenied:
                                ProcessDisconnect((IPEndPoint) endPoint);
                                break;
                        }

                        //todo: как то вернуть ошибку из потока, может нужна shared var в NewNet?
                        // return err
                        //}
                    }
                    catch (Exception e)
                    {
                        throw e;
                    }
                }

//			Debug.Log("imhere");
        }

        public string Send(int connId, byte[] data, bool reliable)
        {
            string err = null;
            if (connId >= maxConnections)
            {
                return "invalid connId";
            }

            if (!usedConnections[connId])
            {
                return "unregistered connId";
            }

            if (reliable)
            {
                connections[connId].reliableSender.InsertRawMsg(new RawSndMsg {data = data});
            }
            else
            {
                connections[connId].SendUnreliable(data);
            }

            return null;
        }

        public NetEvent Receive()
        {
            lock (eventQueue)
            {
                if (eventQueue.Count > 0)
                {
                    return eventQueue.Dequeue();
                }
            }

            return null;
        }

        private void ProcessConnectRequest(IPEndPoint endPoint)
        {
            var freeId = -1;
            for (var i = 0; i < usedConnections.Length; i++)
            {
                if (usedConnections[i])
                {
                    if (connections[i].endPoint.Equals(endPoint))
                    {
                        // Соединение с таким адресом уже установлено => просто скипаем этот пакет
                        return;
                    }
                }
                else
                {
                    // Сохраним первый свободный id - может пригодиться в дальнейшем, для установки подключения
                    if (freeId < 0)
                    {
                        freeId = i;
                    }
                }
            }

            if (freeId >= 0)
            {
                // Нашли свободный слот - добавляем клиента
                // Добавляем в список акцептованных подключений
                usedConnections[freeId] = true;
                connections[freeId] = new Connection(freeId, endPoint, true, this);
                lock (eventQueue)
                {
                    // Закидываем в очередь событий установку соединения
                    eventQueue.Enqueue(new NetEvent
                    {
                        type = NetEventType.Connected,
                        connId = freeId
                    });
                }

                lock (socketLock)
                {
                    // Отправляем ConnectAccept
                    socket.SendTo(new[] {(byte) NetMessage.ConnectAccept}, endPoint);
                    Console.WriteLine("connectAccept sent");
                }

                numConnections++;
            }
            else
            {
                lock (socketLock)
                {
                    // Отправляем ConnectDenied
                    socket.SendTo(new[] {(byte) NetMessage.ConnectDenied}, endPoint);
                }
            }
        }

        private void ProcessConnectAccept(IPEndPoint endPoint)
        {
            for (var i = 0; i < usedConnections.Length; i++)
            {
                if (usedConnections[i] && connections[i].endPoint.Equals(endPoint) && !connections[i].accepted)
                {
                    connections[i].accepted = true;
                    // Запускаем reliable workers
                    connections[i].reliableReceiver.Start();
                    connections[i].reliableSender.Start();
                    lock (eventQueue)
                    {
                        // Закидываем в очередь событий установку соединения
                        eventQueue.Enqueue(new NetEvent
                        {
                            type = NetEventType.Connected,
                            connId = connections[i].id
                        });
                    }

                    return;
                }
            }
        }

        private void ProcessDisconnect(IPEndPoint endPoint)
        {
            for (var i = 0; i < usedConnections.Length; i++)
            {
                if (usedConnections[i] && connections[i].endPoint.Equals(endPoint))
                {
                    usedConnections[i] = false;
                    var connId = connections[i].id;
                    // Останавливаем reliable workers
                    connections[i].reliableSender.Stop();
                    connections[i].reliableReceiver.Stop();
                    connections[i] = null;
                    numConnections--;
                    lock (eventQueue)
                    {
                        // Закидываем в очередь событий отключение
                        eventQueue.Enqueue(new NetEvent
                        {
                            type = NetEventType.Disconnected,
                            connId = connId
                        });
                    }

                    return;
                }
            }
        }

        private string ReceivePayload(byte[] data, IPEndPoint endPoint)
        {
            Connection conn = null;
            // Ищем адрес в списке зарегистрированных подключений
            for (var i = 0; i < usedConnections.Length; i++)
            {
                var c = connections[i];
                if (usedConnections[i] && c.endPoint.Equals(endPoint))
                {
                    conn = c;
                    break;
                }
            }

            if (conn == null || !conn.accepted)
            {
                // Получили пакет данных от незарегистрированного подключения - скипаем
                Console.WriteLine("not registered");
                return null;
            }

            // Клиент акцептован, принимаем данные в обработку
            string err = null;
            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms))
            {
                var qos = (QOS) br.ReadByte();
//				Debug.LogFormat("QOS is {0}", qos);
                // Определяем QOS сообщения
                switch (qos)
                {
                    case QOS.Reliable:
//						var s = "";
//						for (var i = 0; i < data.Length; i++) {
//							s += " " + data[i];
//						}

//						Debug.Log("reliable data received:" + s);
                        //Debug.Log(DateTime.Now.ToString("hh.mm.ss.ffffff") + ":reliable data received:" + data.Length + " bytes");
                        err = conn.reliableReceiver.Receive(br);
                        break;
                    case QOS.Unreliable:
                        conn.ReceiveUnreliable(br);
                        break;
                    case QOS.Ack:
                        conn.reliableSender.ReceiveAck(br);
                        break;
                }
            }

            return err;
        }

        public void StopListen()
        {
            listening = false;
            for (var i = 0; i < usedConnections.Length; i++)
            {
                if (usedConnections[i])
                {
                    Disconnect(i);
                }
            }

            sending = false;
        }
    }
}