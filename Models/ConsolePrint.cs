using System;


namespace TCPServer.Models
{
    public static class  ConsolePrint
    {        

        public static void PrintLine(char splitChar = '-')
        {
            Console.WriteLine(new string(splitChar, TcpSettings.ConsoleTableWidth));
        }

        public static void PrintRow(params string[] columns)
        {
            int width = (TcpSettings.ConsoleTableWidth - columns.Length) / columns.Length;
            string row = "|";

            foreach (string column in columns)
            {
                row += AlignCentre(column,width) + "|";
            }

            Console.WriteLine(row);
        }

        public static string AlignCentre(string text , int defWidth=0)
        {
            int width =defWidth==0?  TcpSettings.ConsoleTableWidth: defWidth;
            text = text.Length > width ? text.Substring(0, width - 3) + "..." : text;

            if (string.IsNullOrEmpty(text))
            {
                return new string(' ', width);
            }
            else
            {
                return text.PadRight(width - (width - text.Length) / 2).PadLeft(width);
            }
        }
    }
}
