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
            ["ConnectionString"] = "mongodb://localhost:27017",
            ["User"] = "root", //não está sendo utilizado
            ["Password"] = "" //não está sendo utilizado
        };
        //Conexão com o MongoDB
        private static readonly IMongoDatabase mongoDatabase = new MongoClient(ConnectionDatabaseConfig["ConnectionString"]).GetDatabase(ConnectionDatabaseConfig["DatabaseName"]);

        public static async Task Start()
        {

        }

        private static async Task ProcessDocument()
        {
            Channel<byte> DataDownload = Channel.CreateUnbounded<byte>(); //por limite com base na memoria RAM
            Channel<BsonDocument> DataProcess = Channel.CreateUnbounded<BsonDocument>();

            ICollection<Task> downloaders = new List<Task>();
            ICollection<Task> processers = new List<Task>();
            ICollection<Task> importers = new List<Task>();

            await Task.WhenAll(downloaders);
            DataDownload.Writer.Complete();
            await Task.WhenAll(processers);
            DataProcess.Writer.Complete();
            await Task.WhenAll(importers);

            Console.WriteLine($"Arquivo {} Importado com Sucesso!");
        }
        private static async Task Downloader(ChannelWriter<byte> writer)
        {

        }
        private static async Task Processer(ChannelReader<byte> reader, ChannelReader<BsonDocument> writer)
        {

        }
        private static async Task Importer(ChannelReader<BsonDocument> reader)
        {

        }
    }
}
