﻿using System;

namespace Typewriter.Metadata.Interfaces
{
    public interface IConstantMetadata : IFieldMetadata
    {
        string Value { get; }
    }
}