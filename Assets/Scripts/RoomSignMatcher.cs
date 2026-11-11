using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Empareja el texto leido por OCR con los nodos del grafo. La IA solo propone
/// un candidato; el usuario confirma el origen antes de iniciar la ruta.
/// </summary>
public static class RoomSignMatcher
{
    public class SignMatch
    {
        public Node node;
        public float score;
        public string reason;

        public SignMatch(Node node, float score, string reason)
        {
            this.node = node;
            this.score = score;
            this.reason = reason;
        }
    }

    private static readonly Regex RoomCodePattern =
        new Regex(@"^([A-Z]{1,5})[-_ ]?(\d{2,4})([A-Z]?)$", RegexOptions.Compiled);

    private static readonly Regex TokenPattern =
        new Regex(@"[A-Z0-9]{2,14}", RegexOptions.Compiled);

    /// <summary>
    /// Devuelve los candidatos ordenados de mayor a menor puntuacion.
    /// <paramref name="prior"/> se usa para preferir el edificio y piso actuales.
    /// </summary>
    public static List<SignMatch> Match(string ocrText, IList<Node> nodes, Node prior,
        int maxResults = 5, float minScore = 0.55f)
    {
        List<SignMatch> results = new List<SignMatch>();
        if (string.IsNullOrEmpty(ocrText) || nodes == null || nodes.Count == 0) return results;

        string text = Normalize(ocrText);
        string compact = Compact(text);
        string canonical = CanonicalDigits(compact);
        List<string> tokens = Tokens(text);

        if (compact.Length == 0) return results;

        for (int i = 0; i < nodes.Count; i++)
        {
            Node node = nodes[i];
            if (node == null) continue;

            List<string> aliases = BuildAliases(node);
            string id = Compact(Normalize(node.EffectiveId));
            string name = Compact(Normalize(node.DisplayName));

            float best = 0f;
            string reason = string.Empty;

            ScoreAgainst(compact, canonical, tokens, id, 1.00f, 0.98f, 0.94f, 0.88f,
                "id", ref best, ref reason);

            if (!string.Equals(name, id, StringComparison.Ordinal))
            {
                ScoreAgainst(compact, canonical, tokens, name, 0.92f, 0.88f, 0.84f, 0.78f,
                    "nombre", ref best, ref reason);
            }

            for (int a = 0; a < aliases.Count; a++)
            {
                string alias = aliases[a];
                if (alias.Length < 2) continue;
                ScoreAgainst(compact, canonical, tokens, alias, 0.95f, 0.90f, 0.86f, 0.80f,
                    "alias", ref best, ref reason);
            }

            // Coincidencia por numero de salon cuando el OCR pierde el prefijo.
            string idDigits = DigitsOnly(id);
            string textDigits = DigitsOnly(compact);
            if (idDigits.Length >= 2 && idDigits == textDigits && !string.IsNullOrEmpty(textDigits))
            {
                float score = compact.Length <= idDigits.Length + 1 ? 0.80f : 0.72f;
                if (score > best)
                {
                    best = score;
                    reason = "numero";
                }
            }

            if (prior != null && best > 0f)
            {
                if (!string.IsNullOrEmpty(prior.buildingId) &&
                    string.Equals(prior.buildingId, node.buildingId, StringComparison.OrdinalIgnoreCase))
                {
                    best += 0.05f;
                }

                if (prior.floor > 0 && prior.floor == node.floor)
                {
                    best += 0.03f;
                }

                best = Math.Min(best, 0.99f);
            }

            if (best >= minScore)
            {
                results.Add(new SignMatch(node, best, reason));
            }
        }

        results.Sort((left, right) =>
        {
            int byScore = right.score.CompareTo(left.score);
            if (byScore != 0) return byScore;
            return string.Compare(left.node.DisplayName, right.node.DisplayName,
                StringComparison.OrdinalIgnoreCase);
        });

        if (results.Count > maxResults) results.RemoveRange(maxResults, results.Count - maxResults);
        return results;
    }

    private static void ScoreAgainst(string compact, string canonical, List<string> tokens,
        string candidate, float exact, float contains, float canonicalExact, float fuzzy,
        string label, ref float best, ref string reason)
    {
        if (string.IsNullOrEmpty(candidate)) return;

        if (candidate.Length >= 3 && compact.Contains(candidate))
        {
            float score = compact == candidate ? exact : contains;
            if (score > best)
            {
                best = score;
                reason = label;
            }
            return;
        }

        if (candidate.Length >= 3 && canonical.Contains(candidate) && canonical != compact)
        {
            float score = canonical == candidate ? canonicalExact : canonicalExact - 0.04f;
            if (score > best)
            {
                best = score;
                reason = label + " (OCR corregido)";
            }
            return;
        }

        for (int i = 0; i < tokens.Count; i++)
        {
            string token = tokens[i];
            if (Math.Abs(token.Length - candidate.Length) > 2) continue;

            float similarity = Similarity(token, candidate);
            if (similarity < 0.74f) continue;

            float score = fuzzy * similarity;
            if (score > best)
            {
                best = score;
                reason = label + " (aprox.)";
            }
        }
    }

    /// <summary>Genera alias utiles aunque el JSON no los traiga.</summary>
    private static List<string> BuildAliases(Node node)
    {
        List<string> aliases = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

        AddAlias(aliases, seen, node.EffectiveId);
        AddAlias(aliases, seen, node.DisplayName);

        if (node.aliases != null)
        {
            for (int i = 0; i < node.aliases.Count; i++) AddAlias(aliases, seen, node.aliases[i]);
        }

        string id = Compact(Normalize(node.EffectiveId));
        Match code = RoomCodePattern.Match(id);
        if (code.Success)
        {
            string prefix = code.Groups[1].Value;
            string number = code.Groups[2].Value;

            AddAlias(aliases, seen, number);
            AddAlias(aliases, seen, prefix + number);
            AddAlias(aliases, seen, prefix + " " + number);
            AddAlias(aliases, seen, prefix + "-" + number);
            AddAlias(aliases, seen, "SALON " + number);
            AddAlias(aliases, seen, "SALON " + prefix + number);
            AddAlias(aliases, seen, "AULA " + number);
            AddAlias(aliases, seen, "LAB " + number);
            AddAlias(aliases, seen, "LABORATORIO " + number);
        }

        string name = Normalize(node.DisplayName);
        string withoutType = Regex.Replace(name,
            @"\b(SALON|AULA|OFICINA|LABORATORIO|BANOS|BANO|HALL|SALIDA|ENTRADA|DE|DEL|LA|EL|LOS|LAS)\b", " ");
        AddAlias(aliases, seen, withoutType);

        // Palabras sueltas utiles: "JUNTAS", "PSICOLOGIA", "UNICOLMAYOR", "BIBLIO".
        string[] words = name.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length >= 4) AddAlias(aliases, seen, words[i]);
        }

        if (id.Contains("BIBLIO")) AddAlias(aliases, seen, "BIBLIO");
        if (id.Contains("UNICOLMAYOR")) AddAlias(aliases, seen, "UNICOLMAYOR");
        if (id.Contains("PSICOLOGIA")) AddAlias(aliases, seen, "PSICOLOGIA");
        if (id.Contains("JUNTAS")) AddAlias(aliases, seen, "JUNTAS");
        if (id.Contains("ESTUDIO")) AddAlias(aliases, seen, "ESTUDIO");

        return aliases;
    }

    private static void AddAlias(List<string> aliases, HashSet<string> seen, string value)
    {
        string normalized = Compact(Normalize(value));
        if (normalized.Length < 2) return;
        if (seen.Add(normalized)) aliases.Add(normalized);
    }

    /// <summary>Mayusculas, sin tildes, solo letras y numeros separados por espacios.</summary>
    public static string Normalize(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        string decomposed = value.Normalize(NormalizationForm.FormD);
        StringBuilder builder = new StringBuilder(decomposed.Length);

        for (int i = 0; i < decomposed.Length; i++)
        {
            char c = decomposed[i];
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsLetterOrDigit(c)) builder.Append(char.ToUpperInvariant(c));
            else builder.Append(' ');
        }

        string spaced = Regex.Replace(builder.ToString(), @"\s+", " ").Trim();

        // Une letras y numeros separados por un espacio: "EN 105" -> "EN 105",
        // el compactado posterior se encarga de "EN105".
        return spaced;
    }

    private static string Compact(string normalized)
    {
        if (string.IsNullOrEmpty(normalized)) return string.Empty;
        return normalized.Replace(" ", string.Empty);
    }

    /// <summary>Corrige confusiones tipicas del OCR dentro de codigos con numeros.</summary>
    private static string CanonicalDigits(string compact)
    {
        if (string.IsNullOrEmpty(compact)) return string.Empty;

        char[] chars = compact.ToCharArray();
        bool hasDigit = false;
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] >= '0' && chars[i] <= '9') { hasDigit = true; break; }
        }

        // Solo se aplica a cadenas que parecen codigos (letras + numeros),
        // para no destruir nombres como BIBLIOTECA.
        if (!hasDigit && !Regex.IsMatch(compact, @"^[A-Z]{1,5}[A-Z0-9]{2,5}$")) return compact;

        for (int i = 0; i < chars.Length; i++)
        {
            switch (chars[i])
            {
                case 'O': chars[i] = '0'; break;
                case 'I': chars[i] = '1'; break;
                case 'L': chars[i] = '1'; break;
                case 'S': chars[i] = '5'; break;
                case 'B': chars[i] = '8'; break;
                case 'Z': chars[i] = '2'; break;
                case 'G': chars[i] = '6'; break;
            }
        }
        return new string(chars);
    }

    private static List<string> Tokens(string normalized)
    {
        List<string> tokens = new List<string>();
        if (string.IsNullOrEmpty(normalized)) return tokens;

        MatchCollection matches = TokenPattern.Matches(normalized);
        for (int i = 0; i < matches.Count; i++)
        {
            string value = matches[i].Value;
            if (value.Length >= 2) tokens.Add(value);
        }
        return tokens;
    }

    private static string DigitsOnly(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        StringBuilder builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] >= '0' && value[i] <= '9') builder.Append(value[i]);
        }
        return builder.ToString();
    }

    private static float Similarity(string left, string right)
    {
        if (left == right) return 1f;
        int max = Math.Max(left.Length, right.Length);
        if (max == 0) return 1f;
        return 1f - Levenshtein(left, right) / (float)max;
    }

    private static int Levenshtein(string left, string right)
    {
        int[] previous = new int[right.Length + 1];
        int[] current = new int[right.Length + 1];

        for (int j = 0; j <= right.Length; j++) previous[j] = j;

        for (int i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= right.Length; j++)
            {
                int cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            int[] swap = previous;
            previous = current;
            current = swap;
        }

        return previous[right.Length];
    }
}
