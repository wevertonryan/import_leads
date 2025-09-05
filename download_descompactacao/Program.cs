using download;
using System.Collections;
using System.Text.RegularExpressions;
using System.Net.Http;
using download_descompactacao;

internal class Program
{
    private static async Task Main()
    {
        //await ReceitaImporter.Agendamento();
        await MongoImport.Start();
    }
    /*private static void Main()
    {
        string caminho = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DadosReceita");

        // Verifica se o diretório existe
        if (Directory.Exists(caminho))
        {
            // Obtém todos os subdiretórios
            string[] subdiretorios = Directory.GetDirectories(caminho);
            foreach(var diretorio in subdiretorios)
            {
                string[] arquivos = Directory.GetFiles(diretorio);
                Console.WriteLine(Path.GetFileName(diretorio));
            }
        }
    }*/
}