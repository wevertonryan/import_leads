using ICSharpCode.SharpZipLib.Zip;
using MongoDB.Bson;
using MongoDB.Driver;
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
            Console.WriteLine("# ReceitaImporter Finalizado #");
        }

        private static async Task ProcessDocument()
        /* [ProcessDocument]
        - Método Responsavel pelo processo completo (download -> processamento -> importação) de um único
        - Também é onde ficarão os Canais (Channel)
        - Fará a Chamada dos Produtores e Consumidores 
        - Fará o Controle dinámico dos produtores e consumidores com base nos recursos disponiveis durante a execução (adição ou retiragem)*/
        {
            Channel<byte> DataDownload = Channel.CreateUnbounded<byte>(); //por limite com base na memoria RAM
            Channel<BsonDocument> DataProcess = Channel.CreateUnbounded<BsonDocument>();

            ICollection<Task> downloaders = new List<Task>();
            ICollection<Task> processors = new List<Task>();
            ICollection<Task> importers = new List<Task>();

            //downloaders.Add(Downloader(DataDownload.Writer));
            //processors.Add(Processor(DataDownload.Reader, DataProcess.Writer));
            //importers.Add(Importer(DataProcess.Reader));

            await Task.WhenAll(downloaders);
            DataDownload.Writer.Complete();
            await Task.WhenAll(processors);
            DataProcess.Writer.Complete();
            await Task.WhenAll(importers);

            Console.WriteLine($"Arquivo Importado com Sucesso!");
        }

        // PRODUTORES E CONSUMIDORES
        private static async Task Downloader(ChannelWriter<byte> writer)
        /* [Downloader]
        - Produtor do Canal DataDownload
        - Irá realizar o download dos arquivos armazenando na RAM em Stream
        - Ele irá armazenar em blocos (bytes), e irá adicionar esse blocos no Canal DataDownload
        - Terá provavelmente apenas 1, para baixar o arquivo inteiro, ou mais para realizar o download em partes 
        (só terá mais se tiver mais recurso disponivel mesmo baixando 3 arquivos simultaneamente)*/
        {

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
    }
}
