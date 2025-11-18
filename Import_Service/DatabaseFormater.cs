using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace Import_Service
{
    static class DatabaseFormater
    {
        private static readonly Encoding Latin1Encoding = Encoding.GetEncoding("ISO-8859-1");

        private static readonly Dictionary<string, string[]> headers = new()
        {
            ["Cnaes"] = ["_id", "descricao"],
            ["Empresas"] = ["cnpjBase", "razaoSocial", "naturezaJuridica", "qualificacaoResponsavel", "capitalSocial", "porteEmpresa", "enteFederativo"],
            ["Estabelecimentos"] = ["cnpjBase", "cnpjOrdem", "cnpjDV", "matrizFilial", "nomeFantasia", "situacaoCadastral", "dataSituacaoCadastral", "motivoSituacaoCadastral", "cidadeExterior", "pais", "dataInicioAtividade", "cnaePrincipal", "cnaeSecundario", "tipoLogradouro", "logradouro", "numero", "complemento", "bairro", "CEP", "UF", "municipio", "ddd1", "telefone1", "ddd2", "telefone2", "dddFAX", "FAX", "correioEletronico", "situacaoEspecial", "dataSituacaoEspecial"],
            ["Motivos"] = ["_id", "descricao"],
            ["Municipios"] = ["_id", "descricao"],
            ["Naturezas"] = ["_id", "descricao"],
            ["Paises"] = ["_id", "descricao"],
            ["Qualificacoes"] = ["_id", "descricao"],
            ["Simples"] = ["cnpjBase", "opcaoDoSimples", "dataOpcaoDoSimples", "dataExclusaoDoSimples", "MEI", "dataOpcaoMEI", "dataExclusaoMei"],
            ["Socios"] = ["cnpjBase", "identificadorSocio", "nomeSocio", "cnpjCpf", "qualificaoSocio", "dataEntradaSociedade", "pais", "representanteLegal", "nomeRepresentante", "qualificacaoResponsavel", "faixaEtaria"]
        };


        public static void Empresas(BsonDocument doc, ReadOnlySpan<byte> fieldSpan, int headerIdx)
        {
            switch (headers["Empresas"][headerIdx])
            {
                case "capitalSocial":
                    doc["capitalSocial"] = ParseLatin1Double(fieldSpan);
                    break;
                default:
                    doc[headers["Empresas"][headerIdx]] = Latin1Encoding.GetString(fieldSpan);
                    break;
            }
        }
        public static void Estabelecimentos(BsonDocument doc, ReadOnlySpan<byte> fieldSpan, int headerIdx)
        {
            switch (headers["Estabelecimentos"][headerIdx])
            {
                case "dataSituacaoCadastral":
                case "dataInicioAtividade":
                case "dataSituacaoEspecial":
                    doc[headers["Estabelecimentos"][headerIdx]] = fieldSpan.IndexOfAnyExcept((byte)'0') == -1 ? "" : StringToDateTime(fieldSpan);
                    break;
                case "faixaEtaria":
                case "numero":
                case "ddd1":
                case "ddd2":
                case "dddFAX":
                    doc[headers["Estabelecimentos"][headerIdx]] = Latin1BytesToInt(fieldSpan);
                    break;
                case "cnaeSecundario":
                    doc["cnaeSecundario"] = CnaesArray(fieldSpan);
                    break;
                default:
                    doc[headers["Estabelecimentos"][headerIdx]] = Latin1Encoding.GetString(fieldSpan);
                    break;
            }
        }

        public static void Socios(BsonDocument doc, ReadOnlySpan<byte> fieldSpan, int headerIdx)
        {
            switch (headers["Socios"][headerIdx])
            {
                case "dataEntradaSociedade":
                    doc["dataEntradaSociedade"] = fieldSpan.IndexOfAnyExcept((byte)'0') == -1 ? "" : StringToDateTime(fieldSpan);
                    break;
                case "identificadorSocio":
                case "faixaEtaria":
                    doc[headers["Socios"][headerIdx]] = Latin1BytesToInt(fieldSpan);
                    break;
                default:
                    doc[headers["Socios"][headerIdx]] = Latin1Encoding.GetString(fieldSpan);
                    break;
            }
        }
        public static void Simples(BsonDocument doc, ReadOnlySpan<byte> fieldSpan, int headerIdx)
        {
            switch (headers["Simples"][headerIdx])
            {
                case "dataOpcaoDoSimples":
                case "dataExclusaoDoSimples":
                case "dataOpcaoMEI":
                case "dataExclusaoMei":
                    doc[headers["Simples"][headerIdx]] = fieldSpan.IndexOfAnyExcept((byte)'0') == -1 ? "" : StringToDateTime(fieldSpan);
                    break;
                default:
                    doc[headers["Simples"][headerIdx]] = Latin1Encoding.GetString(fieldSpan);
                    break;
            }
        }

        
        public static int Latin1BytesToInt(ReadOnlySpan<byte> span)
        {
            int value = 0;

            foreach (byte b in span)
            {
                value = value * 10 + (b - 48); // '0' = 48
            }

            return value;
        }

        private static double ParseLatin1Double(ReadOnlySpan<byte> span)
        {
            double result = 0;
            double sign = 1;
            bool afterDecimal = false;
            double divider = 1;

            if (span.StartsWith((byte)'-'))
            {
                sign = -1;
            }
            foreach (byte b in span)
            {
                if (b == (byte)',' || b == (byte)'.')
                {
                    afterDecimal = true;
                    continue;
                }

                int digit = b - 48; // '0' = 48

                if (!afterDecimal)
                {
                    result = result * 10 + digit;
                }
                else
                {
                    divider *= 10;
                    result += digit / divider;
                }
            }

            //Console.WriteLine($"Como veio: {Latin1Encoding.GetString(span)}\nComo saiu {result * sign}\n");
            return result * sign;
        }
        private static DateTime StringToDateTime(ReadOnlySpan<byte> date)
        {
            static int Parse2(ReadOnlySpan<byte> s)
            {
                return (s[0] - 48) * 10 +
                       (s[1] - 48);
            }

            static int Parse4(ReadOnlySpan<byte> s)
            {
                return (s[0] - 48) * 1000 +
                       (s[1] - 48) * 100 +
                       (s[2] - 48) * 10 +
                       (s[3] - 48);
            }

            int year = Parse4(date[..4]);
            int month = Parse2(date.Slice(4, 2));
            int day = Parse2(date.Slice(6, 2));

            return new DateTime(year, month, day);
        }

        private static BsonArray CnaesArray(ReadOnlySpan<byte> cnaesArray)
        {
            var bsonCnaesArray = new BsonArray();
            int start = 0;
            var empresaSpan = Latin1Encoding.GetString(cnaesArray);
            try
            {
                for (int i = 0; i < cnaesArray.Length; i++)
                {
                    if (cnaesArray[i] == (byte)',')
                    {
                        addFieldToDocument(cnaesArray, start, i);
                        start = i + 1;
                    }
                }
                addFieldToDocument(cnaesArray, start, cnaesArray.Length);
            }
            catch (Exception)
            {
                throw new Exception(Latin1Encoding.GetString(cnaesArray));
            }
            return bsonCnaesArray;
            void addFieldToDocument(ReadOnlySpan<byte> cnae, int start, int end)
            {
                ReadOnlySpan<byte> cnaeSpan = cnae.Slice(start, (end - start));
                bsonCnaesArray.Add(Latin1Encoding.GetString(cnaeSpan));
            }
        }
    }
}
