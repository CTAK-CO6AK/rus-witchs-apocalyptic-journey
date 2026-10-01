using System;

namespace WitchRusPatcher
{
    public static class ConsoleMenu
    {
        public static int Show(string prompt, string[] options, int defaultIndex = 0)
        {
            if (options == null || options.Length == 0)
                throw new ArgumentException("Options cannot be empty.", nameof(options));

            if (defaultIndex < 0 || defaultIndex >= options.Length)
                defaultIndex = 0;

            if (Console.IsInputRedirected || Console.IsOutputRedirected)
            {
                // Fallback for non-interactive or redirected environments
                Console.WriteLine(prompt);
                for (int i = 0; i < options.Length; i++)
                {
                    Console.WriteLine($"  {(i == defaultIndex ? "[*]" : "[ ]")} {options[i]}");
                }
                return defaultIndex;
            }

            Console.WriteLine(prompt);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("(Управление: стрелки Вверх/Вниз или клавиши W/S, подтверждение — Enter)");
            Console.ResetColor();

            int selectedIndex = defaultIndex;
            int startTop = 0;
            try { startTop = Console.CursorTop; } catch { }

            Console.CursorVisible = false;
            try
            {
                while (true)
                {
                    Console.SetCursorPosition(0, startTop);
                    for (int i = 0; i < options.Length; i++)
                    {
                        if (i == selectedIndex)
                        {
                            Console.ForegroundColor = ConsoleColor.Black;
                            Console.BackgroundColor = ConsoleColor.Green;
                            Console.Write(" > ");
                            Console.Write(options[i]);
                            Console.ResetColor();
                            Console.WriteLine("    ");
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Gray;
                            Console.Write("   ");
                            Console.Write(options[i]);
                            Console.ResetColor();
                            Console.WriteLine("    ");
                        }
                    }

                    var keyInfo = Console.ReadKey(true);
                    if (keyInfo.Key == ConsoleKey.UpArrow || keyInfo.Key == ConsoleKey.W)
                    {
                        selectedIndex--;
                        if (selectedIndex < 0)
                            selectedIndex = options.Length - 1;
                    }
                    else if (keyInfo.Key == ConsoleKey.DownArrow || keyInfo.Key == ConsoleKey.S)
                    {
                        selectedIndex++;
                        if (selectedIndex >= options.Length)
                            selectedIndex = 0;
                    }
                    else if (keyInfo.Key == ConsoleKey.Enter)
                    {
                        Console.SetCursorPosition(0, startTop + options.Length);
                        Console.WriteLine();
                        return selectedIndex;
                    }
                }
            }
            finally
            {
                Console.CursorVisible = true;
            }
        }

        public static bool AskYesNo(string prompt, bool defaultYes = true)
        {
            var options = new[] { "Да", "Нет" };
            int defaultIndex = defaultYes ? 0 : 1;
            int chosen = Show(prompt, options, defaultIndex);
            return chosen == 0;
        }
    }
}
