using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeCave.Revit.Toolkit.Parameters.Shared
{
    /// <inheritdoc />
    /// <summary>
    /// This class represents Revit shared parameter file
    /// </summary>
    public sealed partial class SharedParameterFile : ICloneable
    {
        private static readonly Regex SectionRegex;
        private static readonly Configuration CsvConfiguration;

        /// <summary>
        /// Initializes the <see cref="SharedParameterFile"/> class.
        /// </summary>
        static SharedParameterFile()
        {
            SectionRegex = new Regex(@"\*(?<section>[A-Z]+)\t", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            CsvConfiguration = new Configuration
            {
                HasHeaderRecord = true,
                AllowComments = true,
                IgnoreBlankLines = true,
                Delimiter = "\t",
                DetectColumnCountChanges = false,
                QuoteNoFields = true
            };
        }

        /// <summary>
        /// Gets or sets the meta-data section of the shared parameter file.
        /// </summary>
        /// <value>
        /// The meta-data section of the shared parameter file.
        /// </value>
        public Meta Metadata { get; set; } = new Meta {Version = 2, MinVersion = 1};

        /// <summary>
        /// Gets or sets the groups section of the shared parameter file.
        /// </summary>
        /// <value>
        /// The groups section of the shared parameter file.
        /// </value>
        public List<Group> Groups { get; set; } = new List<Group>();

        /// <summary>
        /// Gets or sets the parameters section of the shared parameter file.
        /// </summary>
        /// <value>
        /// The parameters section of the shared parameter file.
        /// </value>
        public List<Parameter> Parameters { get; set; } = new List<Parameter>();

        /// <summary>
        /// Extracts <see cref="SharedParameterFile"/> object from a .txt file.
        /// </summary>
        /// <param name="sharedParameterFile">The shared parameter file path.</param>
        /// <returns>The shared parameter file</returns>
        /// <exception cref="ArgumentException"></exception>
        public static SharedParameterFile FromFile(string sharedParameterFile)
        {
            if (!File.Exists(sharedParameterFile))
            {
                throw new ArgumentException($"The following parameter file doesn't exist: '{sharedParameterFile}'");
            }

            if (string.IsNullOrWhiteSpace(sharedParameterFile) || !Path.GetExtension(sharedParameterFile).ToLowerInvariant().Contains("txt"))
            {
                throw new ArgumentException($"Shared parameter file must be a .txt file, please check the path you have supplied: '{sharedParameterFile}'");
            }

            var sharedParamsText = File.ReadAllText(sharedParameterFile);
            return FromText(sharedParamsText);
        }

        /// <summary>
        /// Extracts <see cref="SharedParameterFile"/> object from a string.
        /// </summary>
        /// <param name="sharedParameterText">Text content of shared parameter file.</param>
        /// <returns>The shared parameter file</returns>
        /// <exception cref="System.ArgumentException">sharedParameterText</exception>
        public static SharedParameterFile FromText(string sharedParameterText)
        {
            if (string.IsNullOrWhiteSpace(sharedParameterText))
            {
                throw new ArgumentException($"{nameof(sharedParameterText)} must be a non empty string");
            }

            var sharedParamsFileLines = SectionRegex
                .Split(sharedParameterText)
                .Where(line => !line.StartsWith("#")) // Exclude comment lines
                .ToArray();

            var sharedParamsFileSections = sharedParamsFileLines
                .Where((e, i) => i % 2 == 0)
                .Select((e, i) => new {Key = e, Value = sharedParamsFileLines[i * 2 + 1]})
                .ToDictionary(kp => kp.Key, kp => kp.Value.Replace($"{kp.Key}\t", string.Empty));

            var sharedParamsFile = new SharedParameterFile();
            if (sharedParamsFileSections == null || sharedParamsFileSections.Count < 3 ||
                !(sharedParamsFileSections.ContainsKey(Sections.META) &&
                  sharedParamsFileSections.ContainsKey(Sections.GROUPS) &&
                  sharedParamsFileSections.ContainsKey(Sections.PARAMS)))
            {
                throw new InvalidDataException("Failed to parse shared parameter file content," +
                                               "because it doesn't contain enough data for being qualified as a valid shared parameter file.");
            }

            foreach (var section in sharedParamsFileSections)
            {
                using (var stringReader = new StringReader(section.Value))
                {
                    using (var csvReader = new CsvReader(stringReader, CsvConfiguration))
                    {
                        csvReader.Configuration.TrimOptions = TrimOptions.Trim;
                        // TODO implement
                        // csvReader.Configuration.AllowComments = true;
                        // csvReader.Configuration.Comment = '#';

                        var originalHeaderValidated = csvReader.Configuration.HeaderValidated;
                        csvReader.Configuration.HeaderValidated = (isValid, headerNames, headerIndex, context) =>
                        {
                            // Everything is OK, just go out
                            if (isValid)
                                return;

                            // Allow DESCRIPTION header to be missing (it's actually missing in older shared parameter files)
                            if (nameof(Parameter.Description).Equals(headerNames?.FirstOrDefault(), StringComparison.OrdinalIgnoreCase))
                                return;

                            // Allow USERMODIFIABLE header to be missing (it's actually missing in older shared parameter files)
                            if (nameof(Parameter.UserModifiable).Equals(headerNames?.FirstOrDefault(), StringComparison.OrdinalIgnoreCase))
                                return;

                            originalHeaderValidated(false, headerNames, headerIndex, context);
                        };

                        var originalMissingFieldFound = csvReader.Configuration.MissingFieldFound;
                        csvReader.Configuration.MissingFieldFound = (headerNames, index, context) =>
                        {
                            // Allow DESCRIPTION header to be missing (it's actually missing in older shared parameter files)
                            if (nameof(Parameter.Description).Equals(headerNames?.FirstOrDefault(), StringComparison.OrdinalIgnoreCase))
                                return;

                            // Allow USERMODIFIABLE header to be missing (it's actually missing in older shared parameter files)
                            if (nameof(Parameter.UserModifiable).Equals(headerNames?.FirstOrDefault(), StringComparison.OrdinalIgnoreCase))
                                return;

                            originalMissingFieldFound(headerNames, index, context);
                        }; 

                        switch (section.Key)
                        {
                            // Parse *META section
                            case Sections.META:
                                csvReader.Configuration.RegisterClassMap<MetaClassMap>();
                                sharedParamsFile.Metadata = csvReader.GetRecords<Meta>().FirstOrDefault();
                                break;

                            // Parse *GROUP section
                            case Sections.GROUPS:
                                csvReader.Configuration.RegisterClassMap<GroupClassMap>();
                                sharedParamsFile.Groups = csvReader.GetRecords<Group>().ToList();
                                break;

                            // Parse *PARAM section
                            case Sections.PARAMS:
                                csvReader.Configuration.RegisterClassMap<ParameterClassMap>();
                                sharedParamsFile.Parameters = csvReader.GetRecords<Parameter>().ToList();
                                break;

                            default:
                                Debug.WriteLine($"Unknown section type: {section.Key}");
                                continue;
                        }
                    }
                }
            }

            // Post-process parameters by assigning group names using group IDs
            // and recover UnitType from ParameterType
            sharedParamsFile.Parameters = sharedParamsFile
                .Parameters
                .Select(p =>
                {
                    p.GroupName = sharedParamsFile.Groups?.FirstOrDefault(g => g.Id == p.GroupId)?.Name;
                    p.UnitType = p.ParameterType.GetUnitType();
                    return p;
                })
                .ToList();

            return sharedParamsFile;
        }

        /// <summary>
        /// Returns a <see cref="String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            var output = new StringBuilder();
            output.AppendLine("# This is a Revit shared parameter file.");
            output.AppendLine("# Do not edit manually.");

            // Serialize META to CSV
            var metaAsString = SectionToCsv<MetaClassMap>(Sections.META, new[] { Metadata });
            output.AppendLine(metaAsString);

            // Serialize GROUP entries to CSV
            var groupsAsString = SectionToCsv<GroupClassMap>(Sections.GROUPS, Groups);
            output.AppendLine(groupsAsString);

            // Serialize PARAM entries to CSV
            var paramsAsString = SectionToCsv<ParameterClassMap>(Sections.PARAMS, Parameters);
            output.AppendLine(paramsAsString);

            return output.ToString();
        }

        /// <summary>
        /// Serializes shared parameter file's sections to CSV.
        /// </summary>
        /// <typeparam name="TCsvMap">CSV class mappings</typeparam>
        /// <param name="sectionName">Name of the section.</param>
        /// <param name="sectionEntries">Section entries.</param>
        /// <returns></returns>
        private static string SectionToCsv<TCsvMap>(string sectionName, IEnumerable sectionEntries)
            where TCsvMap : ClassMap
        {
            // Serialize entries to CSV
            var sectionBuilder = new StringBuilder();
            using (var textWriter = new StringWriter(sectionBuilder))
            {
                using (var csvWriter = new CsvWriter(textWriter, CsvConfiguration))
                {
                    csvWriter.Configuration.RegisterClassMap<TCsvMap>();
                    csvWriter.WriteRecords(sectionEntries);
                }
            }

            // Prepends section lines with section name
            var sectionAsString = string.Join(Environment.NewLine,
                sectionBuilder.ToString()
                    .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => $"{sectionName}\t{line}")
            );

            // Prepends asterisk as section marker
            return $"*{sectionAsString}";
        }

        /// <inheritdoc />
        /// <summary>
        /// Clones this instance.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="T:System.NotImplementedException"></exception>
        public object Clone()
        {
            // TODO Implement ICloneable
            throw new NotImplementedException();
        }
    }
}