﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System.IO;
using Microsoft.AspNetCore.Razor.ProjectSystem;
using Microsoft.AspNetCore.Razor.Serialization.Json;

namespace Microsoft.AspNetCore.Razor.LanguageServer.Serialization;

internal sealed class RazorProjectInfoDeserializer : IRazorProjectInfoDeserializer
{
    public static readonly IRazorProjectInfoDeserializer Instance = new RazorProjectInfoDeserializer();

    private RazorProjectInfoDeserializer()
    {
    }

    public RazorProjectInfo? DeserializeFromFile(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        try
        {
            return JsonDataConvert.DeserializeObject(reader, ObjectReaders.ReadProjectInfoFromProperties);
        }
        catch
        {
            // Swallow deserialization exceptions. There's many reasons they can happen, all out of our control.
            return null;
        }
    }
}
