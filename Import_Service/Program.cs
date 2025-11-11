//using download;
using System.Collections;
using System.Text.RegularExpressions;
using System.Net.Http;
using Import_Service;
using System.Diagnostics;
using System.Globalization;

internal class Program
{
    private static async Task Main()
    {
        await ReceitaImporter.Start();
    }
}