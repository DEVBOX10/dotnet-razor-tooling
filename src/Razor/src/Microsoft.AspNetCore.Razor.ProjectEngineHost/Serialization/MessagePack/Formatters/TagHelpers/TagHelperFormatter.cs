﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using MessagePack;
using Microsoft.AspNetCore.Razor.Language;

namespace Microsoft.AspNetCore.Razor.Serialization.MessagePack.Formatters.TagHelpers;

internal sealed class TagHelperFormatter : ValueFormatter<TagHelperDescriptor>
{
    public static readonly ValueFormatter<TagHelperDescriptor> Instance = new TagHelperFormatter();

    private TagHelperFormatter()
    {
    }

    protected override TagHelperDescriptor Deserialize(ref MessagePackReader reader, SerializerCachingOptions options)
    {
        reader.ReadArrayHeaderAndVerify(12);

        var kind = CachedStringFormatter.Instance.Deserialize(ref reader, options).AssumeNotNull();
        var name = CachedStringFormatter.Instance.Deserialize(ref reader, options).AssumeNotNull();
        var assemblyName = CachedStringFormatter.Instance.Deserialize(ref reader, options).AssumeNotNull();

        var displayName = CachedStringFormatter.Instance.Deserialize(ref reader, options);
        var documentationObject = reader.Deserialize<DocumentationObject>(options);
        var tagOutputHint = CachedStringFormatter.Instance.Deserialize(ref reader, options);
        var caseSensitive = reader.ReadBoolean();

        var tagMatchingRules = reader.Deserialize<TagMatchingRuleDescriptor[]>(options);
        var boundAttributes = reader.Deserialize<BoundAttributeDescriptor[]>(options);
        var allowedChildTags = reader.Deserialize<AllowedChildTagDescriptor[]>(options);

        var metadata = reader.Deserialize<MetadataCollection>(options);
        var diagnostics = reader.Deserialize<RazorDiagnostic[]>(options);

        return new DefaultTagHelperDescriptor(
            kind, name, assemblyName,
            displayName!, documentationObject,
            tagOutputHint, caseSensitive,
            tagMatchingRules, boundAttributes, allowedChildTags,
            metadata, diagnostics);
    }

    protected override void Serialize(ref MessagePackWriter writer, TagHelperDescriptor value, SerializerCachingOptions options)
    {
        writer.WriteArrayHeader(12);

        CachedStringFormatter.Instance.Serialize(ref writer, value.Kind, options);
        CachedStringFormatter.Instance.Serialize(ref writer, value.Name, options);
        CachedStringFormatter.Instance.Serialize(ref writer, value.AssemblyName, options);

        CachedStringFormatter.Instance.Serialize(ref writer, value.DisplayName, options);
        writer.SerializeObject(value.DocumentationObject, options);
        CachedStringFormatter.Instance.Serialize(ref writer, value.TagOutputHint, options);
        writer.Write(value.CaseSensitive);

        writer.Serialize((TagMatchingRuleDescriptor[])value.TagMatchingRules, options);
        writer.Serialize((BoundAttributeDescriptor[])value.BoundAttributes, options);
        writer.Serialize((AllowedChildTagDescriptor[])value.AllowedChildTags, options);

        writer.Serialize((MetadataCollection)value.Metadata, options);
        writer.Serialize((RazorDiagnostic[])value.Diagnostics, options);
    }
}
