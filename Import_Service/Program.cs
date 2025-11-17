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
    private static async Task Main()
    {
        await ReceitaImporter.Start();
    }
}