//using download;
using System.Collections;
using System.Text.RegularExpressions;
using System.Net.Http;
using download_descompactacao;
using System.Diagnostics;

internal class Program
{
    private static async Task Main()
    {
        await ReceitaImporter.Start();
    }
}