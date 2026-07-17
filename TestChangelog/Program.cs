// See https://aka.ms/new-console-template for more information

using System;
using System.CommandLine;

namespace TestChangelog
{
    internal static class Program
    {
        static void Main(string[] args)
        {
            Option<string> fileOption = new("--")
            {
                Description = "The file to read and display on the console"
            };
        }
    }
}