using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace download_descompactacao
{
    internal class MongoImport
    {
        // Limita o número de importações simultâneas para evitar sobrecarga no sistema
        private static readonly SemaphoreSlim semaphore = new SemaphoreSlim(4);

        public static async Task Start()
        {
            string pastaCsv = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DadosReceita");

            if (!Directory.Exists(pastaCsv))
            {
                Console.WriteLine("A pasta especificada não existe.");
                return;
            }

            // Busca todos os arquivos de dados nos subdiretórios
            List<string> arquivos = Directory.GetDirectories(pastaCsv)
                                     .SelectMany(Directory.GetFiles)
                                     .ToList();

            Console.WriteLine($"|=====| INICIANDO A IMPORTAÇÃO DE {arquivos.Count} ARQUIVOS |=====|");
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            // Cria uma lista de Tasks para cada importação
            var tasks = arquivos.Select(arquivo => ImportarComSemaphoroAsync(arquivo));

            // Aguarda a conclusão de todas as tarefas de importação
            await Task.WhenAll(tasks);

            stopwatch.Stop();
            Console.WriteLine("Todas as importações foram concluídas!");
            Console.WriteLine($"Tempo total de importação: {stopwatch.Elapsed.TotalSeconds:F2} segundos");
        }

        private static async Task ImportarComSemaphoroAsync(string arquivoCsv)
        {
            // Espera por um "slot" disponível no semáforo
            await semaphore.WaitAsync();

            try
            {
                Console.WriteLine($"Iniciando Importação do Arquivo {arquivoCsv}");
                string diretorio = Path.GetFileName(Path.GetDirectoryName(arquivoCsv)!);
                await ImportarArquivoAsync(arquivoCsv, diretorio);
            }
            finally
            {
                // Libera o "slot" para que outra tarefa possa começar
                semaphore.Release();
            }
        }

        private static async Task ImportarArquivoAsync(string arquivoCsv, string diretorio)
        {
            string bancoNome = "LeadSearch";
            var psi = new ProcessStartInfo
            {
                FileName = "mongoimport",
                Arguments = $"--db {bancoNome} --collection {diretorio} --type csv --file \"{arquivoCsv}\" --batchSize 5000 --headerline --numInsertionWorkers 4",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var processo = Process.Start(psi);
                string output = await processo.StandardOutput.ReadToEndAsync();
                string error = await processo.StandardError.ReadToEndAsync();
                await processo.WaitForExitAsync();

                Console.WriteLine($" - Arquivo importado com sucesso: {Path.GetFileName(arquivoCsv)}");
                if (!string.IsNullOrEmpty(error))
                {
                    Console.WriteLine($"   > Erro: {error}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($" - Falha crítica ao importar {Path.GetFileName(arquivoCsv)}: {ex.Message}");
            }
        }
    }
}