//using download;
using Import_Service;
using MongoDB.Bson;
using System;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

internal class Program
{
    static DateOnly StringToDateOnly(ReadOnlySpan<byte> date)
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

        return new DateOnly(year, month, day);
    }
    static BsonArray CnaesArray(ReadOnlySpan<byte> cnaesArray)
    {
        var bsonCnaesArray = new BsonArray();
        int start = 1;
        try
        {
            for (int i = 1; i < cnaesArray.Length - 1; i++)
            {
                if (cnaesArray[i] == (byte)',' && cnaesArray[i - 1] == (byte)'\"' && cnaesArray[i + 1] == (byte)'\"')
                {
                    addFieldToDocument(cnaesArray, start, i);
                    start = i + 1;
                }
            }
            addFieldToDocument(cnaesArray, start, cnaesArray.Length - 1);
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
    private static async Task Main()
    {
        await ReceitaImporter.Start();
    }
}