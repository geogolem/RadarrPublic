using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    public class CleanupUnusedTags : IHousekeepingTask
    {
        private readonly IMainDatabase _database;
        private readonly IAutoTaggingRepository _autoTaggingRepository;

        public CleanupUnusedTags(IMainDatabase database, IAutoTaggingRepository autoTaggingRepository)
        {
            _database = database;
            _autoTaggingRepository = autoTaggingRepository;
        }

        public void Clean()
        {
            using var mapper = _database.OpenConnection();
            var usedTags = new[]
                {
                    "Movies", "Notifications", "DelayProfiles", "ReleaseProfiles", "ImportLists", "Indexers",
                    "AutoTagging", "DownloadClients"
                }
                .SelectMany(v => GetUsedTags(v, mapper))
                .Concat(GetAutoTaggingTagSpecificationTags(mapper))
                .Distinct()
                .ToList();
            var cleanLibraryTags = mapper.Query<string>($"SELECT Value FROM Config WHERE Config.Key='cleanlibrarytags'");

            if (usedTags.Any() || !cleanLibraryTags.Empty())
            {
                var usedTagsList = usedTags.Select(d => d.ToString()).Join(",");
                foreach (var t1 in cleanLibraryTags)
                {
                    var cleanLibraryTagsList = string.Empty;
                    if (!(string.IsNullOrEmpty(t1) || t1.Equals("[]")))
                    {
                        cleanLibraryTagsList = string.Join(",", Array.ConvertAll(t1.Replace("[", "").Replace("]", "").Split(' '), s => int.Parse(s)));
                        if (usedTagsList.Length == 0)
                        {
                            usedTagsList = cleanLibraryTagsList;
                        }
                        else
                        {
                            usedTagsList = usedTagsList + "," + cleanLibraryTagsList;
                        }
                    }
                }

                if (_database.DatabaseType == DatabaseType.PostgreSQL)
                {
                    mapper.Execute($"DELETE FROM \"Tags\" WHERE NOT \"Id\" = ANY (\'{{{usedTagsList}}}\'::int[])");
                }
                else
                {
                    mapper.Execute($"DELETE FROM \"Tags\" WHERE NOT \"Id\" IN ({usedTagsList})");
                }
            }
            else
            {
                mapper.Execute("DELETE FROM \"Tags\"");
            }
        }

        private int[] GetUsedTags(string table, IDbConnection mapper)
        {
            return mapper
                .Query<List<int>>(
                    $"SELECT DISTINCT \"Tags\" FROM \"{table}\" WHERE NOT \"Tags\" = '[]' AND NOT \"Tags\" IS NULL")
                .SelectMany(x => x)
                .Distinct()
                .ToArray();
        }

        private List<int> GetAutoTaggingTagSpecificationTags(IDbConnection mapper)
        {
            var tags = new List<int>();
            var autoTags = _autoTaggingRepository.All();

            foreach (var autoTag in autoTags)
            {
                foreach (var specification in autoTag.Specifications)
                {
                    if (specification is TagSpecification tagSpec)
                    {
                        tags.Add(tagSpec.Value);
                    }
                }
            }

            return tags;
        }
    }
}
