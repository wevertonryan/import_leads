//using download;
using System.Collections;
using System.Text.RegularExpressions;
using System.Net.Http;
using download_descompactacao;

internal class Program
{
    private static async Task Main()
    {
        await ReceitaImporter2.Start();
    }
}