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

        private static readonly Channel<byte> Download = Channel.CreateUnbounded<byte>();

        public static async Task Start()
        {
           
        }

        private static async Task Downloader(ChannelWriter<byte> writer)
        {
           
        }
        private static async Task Producer(ChannelReader<byte> reader)
        {
            
        }
    }
}