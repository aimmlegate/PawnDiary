using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PawnDiary
{
    /// <summary>
    /// Minimal dependency-free JSON parser. RimWorld's Unity Mono runtime does not
    /// ship System.Web.Extensions.dll (JavaScriptSerializer) or Json.NET, so we parse
    /// with code that only relies on mscorlib. Objects become Dictionary&lt;string, object&gt;,
    /// arrays become object[], strings become string, numbers become double, and the
    /// literals true/false/null map to bool/null. This matches the shape the callers
    /// previously consumed from JavaScriptSerializer.DeserializeObject.
    /// </summary>
    public static class MiniJson
    {
        public static object Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            int index = 0;
            object value = ParseValue(json, ref index);
            SkipWhitespace(json, ref index);
            if (index != json.Length)
            {
                throw new FormatException("Unexpected trailing characters in JSON at position " + index + ".");
            }

            return value;
        }

        private static object ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length)
            {
                throw new FormatException("Unexpected end of JSON.");
            }

            char c = json[index];
            switch (c)
            {
                case '{':
                    return ParseObject(json, ref index);
                case '[':
                    return ParseArray(json, ref index);
                case '"':
                    return ParseString(json, ref index);
                case 't':
                case 'f':
                    return ParseBool(json, ref index);
                case 'n':
                    ParseLiteral(json, ref index, "null");
                    return null;
                default:
                    return ParseNumber(json, ref index);
            }
        }

        private static Dictionary<string, object> ParseObject(string json, ref int index)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            index++; // consume '{'
            SkipWhitespace(json, ref index);
            if (index < json.Length && json[index] == '}')
            {
                index++;
                return result;
            }

            while (true)
            {
                SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index] != '"')
                {
                    throw new FormatException("Expected property name in JSON at position " + index + ".");
                }

                string key = ParseString(json, ref index);
                SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index] != ':')
                {
                    throw new FormatException("Expected ':' in JSON at position " + index + ".");
                }

                index++; // consume ':'
                object value = ParseValue(json, ref index);
                result[key] = value;

                SkipWhitespace(json, ref index);
                if (index >= json.Length)
                {
                    throw new FormatException("Unexpected end of JSON object.");
                }

                char c = json[index];
                if (c == ',')
                {
                    index++;
                    continue;
                }

                if (c == '}')
                {
                    index++;
                    return result;
                }

                throw new FormatException("Expected ',' or '}' in JSON at position " + index + ".");
            }
        }

        private static object[] ParseArray(string json, ref int index)
        {
            List<object> result = new List<object>();
            index++; // consume '['
            SkipWhitespace(json, ref index);
            if (index < json.Length && json[index] == ']')
            {
                index++;
                return result.ToArray();
            }

            while (true)
            {
                object value = ParseValue(json, ref index);
                result.Add(value);

                SkipWhitespace(json, ref index);
                if (index >= json.Length)
                {
                    throw new FormatException("Unexpected end of JSON array.");
                }

                char c = json[index];
                if (c == ',')
                {
                    index++;
                    continue;
                }

                if (c == ']')
                {
                    index++;
                    return result.ToArray();
                }

                throw new FormatException("Expected ',' or ']' in JSON at position " + index + ".");
            }
        }

        private static string ParseString(string json, ref int index)
        {
            StringBuilder builder = new StringBuilder();
            index++; // consume opening quote
            while (index < json.Length)
            {
                char c = json[index++];
                if (c == '"')
                {
                    return builder.ToString();
                }

                if (c == '\\')
                {
                    if (index >= json.Length)
                    {
                        break;
                    }

                    char escape = json[index++];
                    switch (escape)
                    {
                        case '"':
                            builder.Append('"');
                            break;
                        case '\\':
                            builder.Append('\\');
                            break;
                        case '/':
                            builder.Append('/');
                            break;
                        case 'b':
                            builder.Append('\b');
                            break;
                        case 'f':
                            builder.Append('\f');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        case 'u':
                            if (index + 4 > json.Length)
                            {
                                throw new FormatException("Invalid unicode escape in JSON.");
                            }

                            string hex = json.Substring(index, 4);
                            builder.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            index += 4;
                            break;
                        default:
                            throw new FormatException("Invalid escape character '\\" + escape + "' in JSON.");
                    }
                }
                else
                {
                    builder.Append(c);
                }
            }

            throw new FormatException("Unterminated string in JSON.");
        }

        private static object ParseNumber(string json, ref int index)
        {
            int start = index;
            while (index < json.Length)
            {
                char c = json[index];
                if (c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E' || (c >= '0' && c <= '9'))
                {
                    index++;
                }
                else
                {
                    break;
                }
            }

            string number = json.Substring(start, index - start);
            if (number.Length == 0)
            {
                throw new FormatException("Invalid number in JSON at position " + start + ".");
            }

            return double.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static bool ParseBool(string json, ref int index)
        {
            if (json[index] == 't')
            {
                ParseLiteral(json, ref index, "true");
                return true;
            }

            ParseLiteral(json, ref index, "false");
            return false;
        }

        private static void ParseLiteral(string json, ref int index, string literal)
        {
            if (index + literal.Length > json.Length || json.Substring(index, literal.Length) != literal)
            {
                throw new FormatException("Expected '" + literal + "' in JSON at position " + index + ".");
            }

            index += literal.Length;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length)
            {
                char c = json[index];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                {
                    index++;
                }
                else
                {
                    break;
                }
            }
        }
    }
}
