namespace AstExample
{
    using System;

    static class Program
    {
        [STAThread]
        static void Main()
        {
            AstForm.CreateApplication(
                PoshParser.ParseScript,
                @"Write-Host 'Hello World!'");
        }
    }
}
