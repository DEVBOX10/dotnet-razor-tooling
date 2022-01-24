﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

#nullable disable

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Microsoft.AspNetCore.Razor.LanguageServer.Debugging
{
    internal class RazorBreakpointSpanResponse
    {
        public RazorLanguageKind Kind { get; set; }

        public int PositionIndex { get; set; }

        public Position Position { get; set; }

        public int? HostDocumentVersion { get; set; }
    }
}
