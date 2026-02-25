using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NinePSharp.Server.Utils;

/// <summary>
/// Helper for parsing Ethereum-style ABI function calls from strings.
/// </summary>
public class AbiParser
{
    /// <summary>
    /// Represents a parsed function call with its name and arguments.
    /// </summary>
    public struct FunctionCall
    {
        /// <summary>Gets or sets the name of the function.</summary>
        public string Name { get; set; }
        /// <summary>Gets or sets the list of arguments.</summary>
        public string[] Arguments { get; set; }
    }

    /// <summary>
    /// Parses a function call string like "transfer(0x...,100)"
    /// </summary>
    /// <param name="callString">The string to parse.</param>
    /// <returns>A FunctionCall object or null if parsing fails.</returns>
    public static FunctionCall? ParseCall(string callString)
    {
        var match = Regex.Match(callString, @"^(\w+)\((.*)\)$");
        if (!match.Success) return null;

        var name = match.Groups[1].Value;
        var argsStr = match.Groups[2].Value;
        
        var args = string.IsNullOrEmpty(argsStr) 
            ? Array.Empty<string>() 
            : argsStr.Split(',').Select(s => s.Trim()).ToArray();

        return new FunctionCall
        {
            Name = name,
            Arguments = args
        };
    }
}
