using download;
using System.Collections;
using System.Text.RegularExpressions;
using System.Net.Http;

internal class Program
{
    private static async Task Main()
    {
        await ReceitaImporter.Agendamento();
    }
}