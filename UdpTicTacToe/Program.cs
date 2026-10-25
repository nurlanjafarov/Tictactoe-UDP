using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace UdpTicTacToe
{
    

    public class GameData
    {
        public string Gamer1 { get; set; } = "";
        public string Gamer2 { get; set; } = "";
        public int[][] Game { get; set; } = NewBoard();

        public static int[][] NewBoard() => new[]
        {
            new[] { -1, -1, -1 },
            new[] { -1, -1, -1 },
            new[] { -1, -1, -1 }
        };
    }

    public class Message
    {
        public string Type { get; set; } = "";
        public int Row { get; set; }
        public int Col { get; set; }
        public GameData? Data { get; set; }
    }

    public class Program
    {
        const int Empty = -1;
        const int X = 1;
        const int O = 0;

        static UdpClient udp = null!;
        static IPEndPoint remote = null!;
        static GameData data = new();
        static int[,] board = new int[3, 3];
        static int mySymbol;
        static string lastInfo =  " ";

        static async Task Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
            Console.Title = "UDP Tic-Tac-Toe";

            Console.WriteLine("=== UDP Tic-Tac-Toe ===");
            Console.WriteLine("1 - Yeni oyun yarat (Host, X)");
            Console.WriteLine("2 - Oyuna qosul (Client, O)");
            Console.Write("Sechim (1/2): ");
            string choice = Console.ReadLine()?.Trim() ?? "1";

            Console.Write("Adinizi daxil edin: ");
            string name = Console.ReadLine()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
                name = choice == "2" ? "Player2" : "Player1";

            try
            {
                if (choice == "2")
                    await StartClient(name);
                else
                    await StartHost(name);

                await PlayGame();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nXeta bash verdi: {ex.Message}");
                Console.ResetColor();
            }
            finally
            {
                udp?.Dispose();
            }

            Console.WriteLine("\nCixmaq ucun Enter basin...");
            Console.ReadLine();
        }

        static void ConfigureUdpSocket(UdpClient client)
        {
            if (OperatingSystem.IsWindows())
            {
                const int SIO_UDP_CONNRESET = -1744830452;
                try
                {
                    client.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
                }
                catch
                {
                    
                }
            }
        }

        static async Task StartHost(string name)
        {
            Console.Write("Port (default 5000): ");
            string p = Console.ReadLine()?.Trim() ?? "";
            int port = int.TryParse(p, out int pr) && pr is > 0 and <= 65535 ? pr : 5000;

            udp = new UdpClient(port);
            ConfigureUdpSocket(udp);
            mySymbol = X;

            Console.WriteLine($"\nikinci oyuncu gozlenilir!.... (port {port})...");

            while (true)
            {
                var result = await udp.ReceiveAsync();
                var msg = Deserialize(result.Buffer);

                if (msg?.Type != "JOIN" || msg.Data == null)
                    continue;

                remote = result.RemoteEndPoint;
                data = new GameData()
                {
                    Gamer1 = name,
                    Gamer2 = msg.Data.Gamer2
                };
                break;
            }

            Console.WriteLine($"{data.Gamer2} qoshuldu!");

            // Əvvəlki oyun varsa davam etdirmək
            string file = FileName();

            if (File.Exists(file))
            {
                Console.Write($"'{file}' tapildi. Evvelki oyunu davam etdirek? (b/x): ");
                string answer = (Console.ReadLine() ?? "").Trim().ToLower();
                if (answer == "b" || answer == "y")
                {
                    try
                    {
                        var saved = JsonSerializer.Deserialize<GameData>(File.ReadAllText(file));
                        if (saved != null)
                        {
                            data = saved;
                            LoadBoardFromData();
                            if (CheckResult() != null)
                            {
                                Console.WriteLine("Evvelki oyun artiq basa catib. Yeni oyuna baslanilir.");
                                data.Game = GameData.NewBoard();
                            }
                        }
                    }
                    catch
                    {
                        Console.WriteLine("Ex fayl oxunmadi, yeni oyun baslanir.");
                        data.Game = GameData.NewBoard();
                    }
                }
                else
                {
                    data.Game = GameData.NewBoard();
                }
            }

            LoadBoardFromData();

            await Send(new Message { Type = "START", Data = data });

            Console.WriteLine("\nOyun baslayir! Sen X-sen.");
            await Task.Delay(1000);
        }

        static async Task StartClient(string name)
        {
            Console.Write("Host IP (default 127.0.0.1): ");
            string ip = Console.ReadLine()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(ip))
                ip = "127.0.0.1";

            if (!IPAddress.TryParse(ip, out var address))
            {
                Console.WriteLine("IP unvani duzgun daxil edilmedi!");
                address = IPAddress.Loopback;
            }

            Console.Write("Host portu (default 5000): ");
            string p = Console.ReadLine()?.Trim() ?? "";
            int port = int.TryParse(p, out int pr) && pr is > 0 and <= 65535 ? pr : 5000;

            udp = new UdpClient();
            ConfigureUdpSocket(udp);
            remote = new IPEndPoint(address, port);
            mySymbol = O;

            Console.WriteLine($"\nHost-a qosulunur... ({address}:{port})...");

            var joinMsg = new Message { Type = "JOIN", Data = new GameData { Gamer2 = name } };

            while (true)
            {
                await Send(joinMsg);

                var receiveTask = udp.ReceiveAsync();
                var timeoutTask = Task.Delay(2000);

                var completed = await Task.WhenAny(receiveTask, timeoutTask);
                if (completed == receiveTask)
                {
                    var result = await receiveTask;
                    var msg = Deserialize(result.Buffer);

                    if (msg?.Type == "START" && msg.Data != null)
                    {
                        data = msg.Data;
                        remote = result.RemoteEndPoint;
                        break;
                    }
                }
                else
                {
                    Console.WriteLine("Host hele cvb vermedi, tekrar sorgu gonderilir...");
                }
            }

            LoadBoardFromData();

            Console.WriteLine($"\nOyun baslayir! {data.Gamer1} (X) vs {data.Gamer2} (O). Sen O-san.");
            await Task.Delay(1000);
        }

        static async Task PlayGame()
        {
            // X və O sayı bərabərdirsə X gedir
            int turn = Count(X) == Count(O) ? X : O;

            while (true)
            {
                PrintBoard();

                string? result = CheckResult();

                if (result != null)
                {
                    Console.WriteLine();
                    if (result == "Draw")
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("Hech-heche!");
                    }
                    else
                    {
                        string winnerName = result == "X" ? data.Gamer1 : data.Gamer2;
                        bool isMe = result == (mySymbol == X ? "X" : "O");

                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"Qazanan: {winnerName} ({result})");

                        if (isMe)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("Tebrikler, qazandiniz! 🎉");
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("Uduzdunuz.");
                        }
                    }
                    Console.ResetColor();
                    return;
                }

                if (turn == mySymbol)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Senin novben.");
                    Console.ResetColor();

                    while (true)
                    {
                        Console.Write("Setir və sutun (1-3, meselen: 2 3): ");
                        var parts = (Console.ReadLine() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2 && int.TryParse(parts[0], out int r) && int.TryParse(parts[1], out int c)
                            && r is >= 1 and <= 3 && c is >= 1 and <= 3 && board[r - 1, c - 1] == Empty)
                        {
                            board[r - 1, c - 1] = mySymbol;
                            lastInfo = $"Sen gedis etdin: {r} {c}";

                            SaveGame();

                            await Send(new Message { Type = "MOVE", Row = r - 1, Col = c - 1, Data = data });

                            break;
                        }
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Yanlis və ya dolu xana! Yeniden cehhd et.");
                        Console.ResetColor();
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Reqib gozlenilirr...");
                    Console.ResetColor();

                    while (true)
                    {
                        var res = await udp.ReceiveAsync();
                        var msg = Deserialize(res.Buffer);

                        if (msg?.Type != "MOVE")
                            continue;
                        if (msg.Row is < 0 or > 2 || msg.Col is < 0 or > 2)
                            continue;
                        if (board[msg.Row, msg.Col] != Empty)
                            continue;

                        board[msg.Row, msg.Col] = turn;

                        SaveGame();

                        lastInfo = $"Reqib hemle etdi: {msg.Row + 1} {msg.Col + 1}";

                        break;
                    }
                }

                turn = turn == X ? O : X;
            }
        }

        static string? CheckResult()
        {
            int[][] lines =
            {
                new[] { 0, 0, 0, 1, 0, 2 }, new[] { 1, 0, 1, 1, 1, 2 }, new[] { 2, 0, 2, 1, 2, 2 }, // Sətirlər
                new[] { 0, 0, 1, 0, 2, 0 }, new[] { 0, 1, 1, 1, 2, 1 }, new[] { 0, 2, 1, 2, 2, 2 }, // Sütunlar
                new[] { 0, 0, 1, 1, 2, 2 }, new[] { 0, 2, 1, 1, 2, 0 }                              // Diaqonallar
            };

            foreach (var l in lines)
            {
                int a = board[l[0], l[1]], b = board[l[2], l[3]], c = board[l[4], l[5]];
                if (a != Empty && a == b && b == c)
                    return a == X ? "X" : "O";
            }

            return Count(Empty) == 0 ? "Draw" : null;
        }

        static int Count(int value)
        {
            int n = 0;

            foreach (int cell in board)
                if (cell == value)
                    n++;

            return n;
        }

        static void PrintBoard()
        {
            Console.Clear();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=== UDP Tic-Tac-Toe ===");
            Console.ResetColor();

            Console.WriteLine($"{data.Gamer1} (X)  vs  {data.Gamer2} (O)");
            Console.WriteLine($"Sən: {(mySymbol == X ? "X" : "O")}");
            Console.WriteLine();
            Console.WriteLine("    1   2   3");

            for (int i = 0; i < 3; i++)
            {
                Console.Write($" {i + 1}  ");
                for (int j = 0; j < 3; j++)
                {
                    if (board[i, j] == X)
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write('X');
                    }
                    else if (board[i, j] == O)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Write('O');
                    }
                    else
                    {
                        Console.Write(' ');
                    }
                    Console.ResetColor();

                    if (j < 2)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write(" | ");
                        Console.ResetColor();
                    }
                }
                Console.WriteLine();
                if (i < 2)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("    --+---+--");
                    Console.ResetColor();
                }
            }

            Console.WriteLine();

            if (!string.IsNullOrEmpty(lastInfo))
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine(lastInfo);
                Console.ResetColor();
            }
        }

        static async Task Send(Message msg)
        {
            SyncDataFromBoard();

            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg));

            await udp.SendAsync(bytes, bytes.Length, remote);
        }

        static Message? Deserialize(byte[] buffer)
        {
            try
            {
                return JsonSerializer.Deserialize<Message>(Encoding.UTF8.GetString(buffer));
            }
            catch
            {
                return null;
            }
        }

        // int[,] -> int[][] (JSON üçün [,] dəstəklənmir)
        static void SyncDataFromBoard()
        {
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    data.Game[i][j] = board[i, j];
        }

        // int[][] -> int[,]
        static void LoadBoardFromData()
        {
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    board[i, j] = data.Game[i][j];
        }

        static string FileName()
        {
            string g1 = string.Join("_", data.Gamer1.Split(Path.GetInvalidFileNameChars()));
            string g2 = string.Join("_", data.Gamer2.Split(Path.GetInvalidFileNameChars()));
            return $"{g1}-{g2}.json";
        }

        static void SaveGame()
        {
            SyncDataFromBoard();

            var options = new JsonSerializerOptions { WriteIndented = true };

            File.WriteAllText(FileName(), JsonSerializer.Serialize(data, options));
        }
    }
}
