namespace Hacks
{
    // Note: no usings needed.

    public static class Logging
    {
        // This is a dirty hack that can be dropped into any place in codebase without good logging
        // or just when you don't have time to figure out all the XML config options and just want the job done.
        // Note: DO NOT ACCIDENTALLY CHECK THIS IN :)
        public static void Log(string format, params object[] args)
        {
            const string FILENAME = "TheLog.log";
            var msg = string.Format(format, args);
            var fs = new System.IO.FileStream(FILENAME, System.IO.FileMode.Append, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
            using (var writer = new System.IO.StreamWriter(fs))
            {
                writer.WriteLine(format, args);
                writer.Flush();
                writer.Close();
            }
        }
    }

    public static class Program
    {
        public static void Main()
        {
            Logging.Log("{0}, {1}.", "Hello", "log");
        }
    }
}
