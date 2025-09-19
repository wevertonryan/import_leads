using ICSharpCode.SharpZipLib.Zip;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Configuration;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace download_descompactacao
{
    public static class ReceitaImporter
    {
        private static readonly Dictionary<string, int> ComputerConfig = new()
        {
            ["Cores"] = Environment.ProcessorCount,
            ["RAM"] = 0,
            ["Disk"] = 0
        };
        //Configurações para conexão com o MongoDB
        //OBS: posteriormente não ficará aqui por questões de segurança
        private static readonly Dictionary<string, string> ConnectionDatabaseConfig = new()
        {
            ["DatabaseName"] = "LeadSearch",
            ["ConnectionString"] = "mongodb://localhost:27017"
        };
        //Conexão com o MongoDB
        private static readonly IMongoDatabase mongoDatabase = new MongoClient(ConnectionDatabaseConfig["ConnectionString"]).GetDatabase(ConnectionDatabaseConfig["DatabaseName"]);
        private static readonly HttpClient httpClient = new()
        {
            Timeout = TimeSpan.FromMinutes(10),
            DefaultRequestVersion = HttpVersion.Version20 // Melhor desempenho HTTP/2
        };
        private static string baseUrl = "https://arquivos.receitafederal.gov.br/dados/cnpj/dados_abertos_cnpj/2025-09/";

        /* CÓDIGO (Métodos Públicos)
           * Start(): Começa do Zero ou Continua de onde parou
           * Pause(): Pausa todo o Processo
           * Restart(): Começa tudo de novo
           * Cancel(): Cancela a execução do Processo
           * Progress(): Devolve um objeto/dicionario com o progresso da execução até o momento (Arquivos baixados, Tempo Decorrido até o momento)
           * Log(): Retorna as Mensagens de Erro
           * Config(): Para configurar alguma coisa da importação (ConnectionDatabaseConfig, Limitação dos recursos disponíveis, etc...)
         */
        public static async Task Start() 
        /* [Start]
        - Será o Main dessa Classe
        - Terá como Papel chamar os metodos para executar o processo de todos os arquivos */
        {
            Console.WriteLine("# Iniciando ReceitaImporter #");
            /*if (!(await CheckMongoConnection()))
            {
                return;
            }*/
            await ProcessFile("Cnaes");
            Console.WriteLine("# ReceitaImporter Finalizado #");
        }

        private static async Task ProcessFile(string file)
        /* [ProcessDocument]
        - Método Responsavel pelo processo completo (download -> processamento -> importação) de um único
        - Também é onde ficarão os Canais (Channel)
        - Fará a Chamada dos Produtores e Consumidores 
        - Fará o Controle dinámico dos produtores e consumidores com base nos recursos disponiveis durante a execução (adição ou retiragem)*/
        {
            Channel<byte> DataDownload = Channel.CreateUnbounded<byte>(); //por limite com base na memoria RAM
            Channel<BsonDocument> DataProcess = Channel.CreateUnbounded<BsonDocument>();
            int splitDownload = 2; //tem perigo de cortar no meio de um linha (registro), pois não tenho como dar split por linha, vou ter verificar onde é o fim daquela linha

            ICollection<Task> downloaders = new List<Task>();
            ICollection<Task> processors = new List<Task>();
            ICollection<Task> importers = new List<Task>();

            downloaders.Add(Downloader(DataDownload.Writer, 2));
            //processors.Add(Processor(DataDownload.Reader, DataProcess.Writer));
            //importers.Add(Importer(DataProcess.Reader));

            await Task.WhenAll(downloaders);
            DataDownload.Writer.Complete();
            //await Task.WhenAll(processors);
            //DataProcess.Writer.Complete();
            //await Task.WhenAll(importers);

            Console.WriteLine($"Arquivo Importado com Sucesso!");
        }

        private static async Task Trabalho()
        {

        }

        // PRODUTORES E CONSUMIDORES

        private static async Task Downloader(ChannelWriter<byte> writer, int block)
        /* [Downloader]
        - Produtor do Canal DataDownload
        - Irá realizar o download dos arquivos armazenando na RAM em Stream
        - Ele irá armazenar em blocos (bytes), e irá adicionar esse blocos no Canal DataDownload
        - Terá provavelmente apenas 1, para baixar o arquivo inteiro, ou mais para realizar o download em partes 
        (só terá mais se tiver mais recurso disponivel mesmo baixando 3 arquivos simultaneamente)*/
        {
            var request = new HttpRequestMessage(HttpMethod.Head, baseUrl);
            var response = await RetryAsync(() => httpClient.SendAsync(request), 10);

            if (response.IsSuccessStatusCode)
            {
                foreach (var header in response.Content.Headers)
                {
                    Console.WriteLine($"{header.Key}: {string.Join(", ", header.Value)}");
                }
            }
            else
            {
                Console.WriteLine("Erro ao obter os cabeçalhos: " + response.StatusCode);
            }

        }

        private static async Task Processor(ChannelReader<byte> reader, ChannelWriter<BsonDocument> writer)
        /* [Processor]
        - Consumidor do Canal DataDownload e Produtor do Canal DataProcess
        - Irá realizar o processamento dos blocos fornecidos pelo Downloader
        - Será feita a descompactação (leitura do arquivo)
        - E criação do BsonDocument
        - E a subtituição das aspas para vazio
        - Provavel de ter mais de 1 para esse processo por arquivo*/
        {

        }
        private static async Task Importer(ChannelReader<BsonDocument> reader)
        /* [Importer]
        - Consumidor do Canal DataProcess
        - Irá realizar a importação dos Bson Document para o MongoDB
        - Provavel que terá mais de um para esse processo*/
        {

        }

        // CheckConnection

        public static async Task<bool> CheckMongoConnection()
        {
            try
            {
                var settings = MongoClientSettings.FromConnectionString(ConnectionDatabaseConfig["ConnectionString"]);
                settings.ConnectTimeout = TimeSpan.FromSeconds(5);
                settings.SocketTimeout = TimeSpan.FromSeconds(5);
                settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);

                var databaseTeste = new MongoClient(settings).GetDatabase(ConnectionDatabaseConfig["DatabaseName"]);

                await databaseTeste.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
                Console.WriteLine("- Sucessfully MongoDB Conection");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"- Erro MongoDB: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> CheckHttpConnection()
        {
            try
            {
                var response = await httpClient.GetAsync(baseUrl);
                Console.WriteLine("- Sucessfully Http Conection");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"- Erro Http: {ex.Message}");
                return false;
            }
        }

        // Melhorar Retry

        private static async Task<T> RetryAsync<T>(Func<Task<T>> action, int maxAttempts, int delayMs = 2000)
        {
            List<Exception> exceptions = new();
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    return await action();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Falha na {attempt}° tentativa");
                    exceptions.Add(ex);
                    if (attempt < maxAttempts - 1)
                    {
                        await Task.Delay(delayMs * (int)Math.Pow(2, attempt)); // Backoff exponencial
                    }
                }
            }
            throw new AggregateException($"Falha após {maxAttempts} tentativas.", exceptions);
        }
    }
}
