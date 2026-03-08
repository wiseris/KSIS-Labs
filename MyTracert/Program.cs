using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MyTraceroute
{
    class Program
    {
        const int MaxHops = 30;
        const int Attempts = 3;
        const int Timeout = 4000;

        static ushort identifier;
        static ushort sequence;

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: mytraceroute [-d] <host>");
                return;
            }

            bool resolveNames = false;
            string target;

            if (args[0] == "-d")
            {
                resolveNames = true;
                if (args.Length < 2)
                {
                    Console.WriteLine("Specify target host.");
                    return;
                }
                target = args[1];
            }
            else
            {
                target = args[0];
            }

            IPAddress? targetAddress;
            try
            {
                if (!IPAddress.TryParse(target, out targetAddress))
                {
                    var hostEntry = Dns.GetHostEntry(target);
                    targetAddress = hostEntry.AddressList.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);

                    if (targetAddress == null)
                    {
                        Console.WriteLine("No IPv4 address found for host.");
                        return;
                    }
                }
            }
            catch
            {
                Console.WriteLine("Unable to resolve host.");
                return;
            }

            Console.WriteLine($"\nTracing route to {target} [{targetAddress}]");
            Console.WriteLine($"over a maximum of {MaxHops} hops:\n");

            identifier = (ushort)Process.GetCurrentProcess().Id;

            using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Icmp)
            {
                ReceiveTimeout = Timeout
            };

            EndPoint remoteEP = new IPEndPoint(targetAddress, 0);
            byte[] buffer = new byte[8192];

            for (int ttl = 1; ttl <= MaxHops; ttl++)
            {
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.IpTimeToLive, ttl);
                Console.Write($"{ttl,2}  ");

                IPAddress? hopAddress = null;
                bool destinationReached = false;

                long[] responseTimes = new long[Attempts];
                bool[] received = new bool[Attempts];

                for (int attempt = 0; attempt < Attempts; attempt++)
                {
                    ushort seq = ++sequence;
                    byte[] packet = CreateIcmpPacket(seq);

                    Stopwatch sw = Stopwatch.StartNew();

                    try
                    {
                        socket.SendTo(packet, remoteEP);

                        EndPoint sender = new IPEndPoint(IPAddress.Any, 0);

                        while (sw.ElapsedMilliseconds < Timeout)
                        {
                            if (socket.Poll(100000, SelectMode.SelectRead))
                            {
                                int receivedBytes = socket.ReceiveFrom(buffer, ref sender);
                                long receiveTime = sw.ElapsedMilliseconds;

                                IPEndPoint responder = (IPEndPoint)sender;

                                if (hopAddress == null)
                                    hopAddress = responder.Address;

                                int ipHeaderLen = (buffer[0] & 0x0F) * 4;
                                int icmpOffset = ipHeaderLen;

                                byte type = buffer[icmpOffset];

                                if (type == 11)
                                {
                                    int idx = MatchReturnedPacket(buffer, icmpOffset);
                                    if (idx == seq)
                                    {
                                        responseTimes[attempt] = receiveTime;
                                        received[attempt] = true;
                                        break;
                                    }
                                }
                                else if (type == 0)
                                {
                                    ushort respId = (ushort)((buffer[icmpOffset + 4] << 8) | buffer[icmpOffset + 5]);

                                    if (respId == identifier)
                                    {
                                        responseTimes[attempt] = receiveTime;
                                        received[attempt] = true;
                                        destinationReached = true;
                                        break;
                                    }
                                }
                            }
                        }

                        if (!received[attempt])
                            responseTimes[attempt] = -1;
                    }
                    catch
                    {
                        responseTimes[attempt] = -1;
                    }
                }

                for (int i = 0; i < Attempts; i++)
                {
                    if (responseTimes[i] >= 0)
                        Console.Write($"{responseTimes[i],4} ms  ");
                    else
                        Console.Write("   *    ");
                }

                if (hopAddress != null)
                {
                    if (resolveNames)
                        Console.Write($" {ResolveHost(hopAddress)} [{hopAddress}]");
                    else
                        Console.Write($" {hopAddress}");
                }
                else
                {
                    Console.Write(" Request timed out.");
                }

                Console.WriteLine();

                if (destinationReached)
                {
                    Console.WriteLine("\nTrace complete.");
                    break;
                }
            }
        }

        static byte[] CreateIcmpPacket(ushort seq)
        {
            byte[] packet = new byte[8 + 32];

            packet[0] = 8;
            packet[1] = 0;

            packet[4] = (byte)(identifier >> 8);
            packet[5] = (byte)(identifier);

            packet[6] = (byte)(seq >> 8);
            packet[7] = (byte)(seq);

            byte[] data = Encoding.ASCII.GetBytes("abcdefghijklmnopqrstuvwabcdefghi");
            Array.Copy(data, 0, packet, 8, Math.Min(data.Length, packet.Length - 8));

            ushort checksum = CalculateChecksum(packet);

            packet[2] = (byte)(checksum >> 8);
            packet[3] = (byte)(checksum);

            return packet;
        }

        static int MatchReturnedPacket(byte[] buffer, int icmpOffset)
        {
            try
            {
                int innerIpOffset = icmpOffset + 8;
                int innerIpLen = (buffer[innerIpOffset] & 0x0F) * 4;
                int innerIcmpOffset = innerIpOffset + innerIpLen;

                ushort id = (ushort)((buffer[innerIcmpOffset + 4] << 8) | buffer[innerIcmpOffset + 5]);
                ushort seq = (ushort)((buffer[innerIcmpOffset + 6] << 8) | buffer[innerIcmpOffset + 7]);

                if (id == identifier)
                {
                    return seq;
                }
            }
            catch { }

            return -1;
        }

        static ushort CalculateChecksum(byte[] data)
        {
            int sum = 0;
            int i = 0;

            while (i < data.Length - 1)
            {
                sum += (data[i] << 8) | data[i + 1];
                i += 2;
            }

            if (i < data.Length)
                sum += data[i] << 8;

            while ((sum >> 16) != 0)
                sum = (sum & 0xFFFF) + (sum >> 16);

            return (ushort)(~sum);
        }

        static string ResolveHost(IPAddress ip)
        {
            try { return Dns.GetHostEntry(ip).HostName; }
            catch { return ip.ToString(); }
        }
    }
}