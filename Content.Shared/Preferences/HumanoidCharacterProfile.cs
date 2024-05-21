using System.Linq;
using System.Text.RegularExpressions;
using Content.Shared.CCVar;
using Content.Shared.Corvax.TTS;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Traits;
using Robust.Shared.Collections;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared.Preferences
{
    /// <summary>
    /// Character profile. Looks immutable, but uses non-immutable semantics internally for serialization/code sanity purposes.
    /// </summary>
    [DataDefinition]
    [Serializable, NetSerializable]
    public sealed partial class HumanoidCharacterProfile : ICharacterProfile
    {
        private static readonly Regex RestrictedNameRegex = new("[^А-Яа-яёЁ0-9' -]");
        private static readonly Regex ICNameCaseRegex = new(@"^(?<word>\w)|\b(?<word>\w)(?=\w*$)");

        public const int MaxNameLength = 32;
        public const int MaxDescLength = 512;

        /// <summary>
        /// Job preferences for initial spawn.
        /// </summary>
        [DataField]
        private Dictionary<string, JobPriority> _jobPriorities = new()
        {
            {
                SharedGameTicker.FallbackOverflowJob, JobPriority.High
            }
        };

        /// <summary>
        /// Antags we have opted in to.
        /// </summary>
        [DataField]
        private HashSet<string> _antagPreferences = new();

        /// <summary>
        /// Enabled traits.
        /// </summary>
        [DataField]
        private HashSet<string> _traitPreferences = new();

        /// <summary>
        /// <see cref="_loadouts"/>
        /// </summary>
        public IReadOnlyDictionary<string, RoleLoadout> Loadouts => _loadouts;

        [DataField]
        private Dictionary<string, RoleLoadout> _loadouts = new();

        [DataField]
        public string Name { get; set; } = "John Doe";

        /// <summary>
        /// Detailed text that can appear for the character if <see cref="CCVars.FlavorText"/> is enabled.
        /// </summary>
        [DataField]
        public string FlavorText { get; set; } = string.Empty;

        /// <summary>
        /// Associated <see cref="SpeciesPrototype"/> for this profile.
        /// </summary>
        [DataField]
        public string Species { get; set; } = SharedHumanoidAppearanceSystem.DefaultSpecies;

        [DataField]
        public string Voice { get; set; } = SharedHumanoidAppearanceSystem.DefaultVoice;

        [DataField]
        public int Age { get; set; } = 18;

        [DataField]
        public Sex Sex { get; private set; } = Sex.Male;

        [DataField]
        public Gender Gender { get; private set; } = Gender.Male;

        /// <summary>
        /// <see cref="Appearance"/>
        /// </summary>
        public ICharacterAppearance CharacterAppearance => Appearance;

        /// <summary>
        /// Stores markings, eye colors, etc for the profile.
        /// </summary>
        [DataField]
        public HumanoidCharacterAppearance Appearance { get; set; } = new();

        /// <summary>
        /// When spawning into a round what's the preferred spot to spawn.
        /// </summary>
        [DataField]
        public SpawnPriorityPreference SpawnPriority { get; private set; } = SpawnPriorityPreference.None;

        /// <summary>
        /// <see cref="_jobPriorities"/>
        /// </summary>
        public IReadOnlyDictionary<string, JobPriority> JobPriorities => _jobPriorities;

        /// <summary>
        /// <see cref="_antagPreferences"/>
        /// </summary>
        public IReadOnlySet<string> AntagPreferences => _antagPreferences;

        /// <summary>
        /// <see cref="_traitPreferences"/>
        /// </summary>
        public IReadOnlySet<string> TraitPreferences => _traitPreferences;

        /// <summary>
        /// If we're unable to get one of our preferred jobs do we spawn as a fallback job or do we stay in lobby.
        /// </summary>
        [DataField]
        public PreferenceUnavailableMode PreferenceUnavailable { get; private set; } =
            PreferenceUnavailableMode.SpawnAsOverflow;

        public HumanoidCharacterProfile(
            string name,
            string flavortext,
            string species,
            string voice, // Corvax-TTS
            int age,
            Sex sex,
            Gender gender,
            HumanoidCharacterAppearance appearance,
            SpawnPriorityPreference spawnPriority,
            Dictionary<string, JobPriority> jobPriorities,
            PreferenceUnavailableMode preferenceUnavailable,
            HashSet<string> antagPreferences,
            HashSet<string> traitPreferences,
            Dictionary<string, RoleLoadout> loadouts)
        {
            Name = name;
            FlavorText = flavortext;
            Species = species;
            Voice = voice; // Corvax-TTS
            Age = age;
            Sex = sex;
            Gender = gender;
            Appearance = appearance;
            SpawnPriority = spawnPriority;
            _jobPriorities = jobPriorities;
            PreferenceUnavailable = preferenceUnavailable;
            _antagPreferences = antagPreferences;
            _traitPreferences = traitPreferences;
            _loadouts = loadouts;
        }

        /// <summary>Copy constructor</summary>
        public HumanoidCharacterProfile(HumanoidCharacterProfile other)
            : this(other.Name,
                other.FlavorText,
                other.Species,
                other.Voice,
                other.Age,
                other.Sex,
                other.Gender,
                other.Appearance.Clone(),
                other.SpawnPriority,
                new Dictionary<string, JobPriority>(other.JobPriorities),
                other.PreferenceUnavailable,
                new HashSet<string>(other.AntagPreferences),
                new HashSet<string>(other.TraitPreferences),
                new Dictionary<string, RoleLoadout>(other.Loadouts))
        {
        }

        /// <summary>
        ///     Get the default humanoid character profile, using internal constant values.
        ///     Defaults to <see cref="SharedHumanoidAppearanceSystem.DefaultSpecies"/> for the species.
        /// </summary>
        /// <returns></returns>
        public HumanoidCharacterProfile()
        {
        }

        /// <summary>
        ///     Return a default character profile, based on species.
        /// </summary>
        /// <param name="species">The species to use in this default profile. The default species is <see cref="SharedHumanoidAppearanceSystem.DefaultSpecies"/>.</param>
        /// <returns>Humanoid character profile with default settings.</returns>
        public static HumanoidCharacterProfile DefaultWithSpecies(string species = SharedHumanoidAppearanceSystem.DefaultSpecies)
        {
            return new()
            {
                Species = species,
            };
        }

        // TODO: This should eventually not be a visual change only.
        public static HumanoidCharacterProfile Random(HashSet<string>? ignoredSpecies = null)
        {
            var prototypeManager = IoCManager.Resolve<IPrototypeManager>();
            var random = IoCManager.Resolve<IRobustRandom>();

            var species = random.Pick(prototypeManager
                .EnumeratePrototypes<SpeciesPrototype>()
                .Where(x => ignoredSpecies == null ? x.RoundStart : x.RoundStart && !ignoredSpecies.Contains(x.ID))
                .ToArray()
            ).ID;

            return RandomWithSpecies(species);
        }

        public static HumanoidCharacterProfile RandomWithSpecies(string species = SharedHumanoidAppearanceSystem.DefaultSpecies)
        {
            var prototypeManager = IoCManager.Resolve<IPrototypeManager>();
            var random = IoCManager.Resolve<IRobustRandom>();

            var sex = Sex.Unsexed;
            var age = 18;
            if (prototypeManager.TryIndex<SpeciesPrototype>(species, out var speciesPrototype))
            {
                sex = random.Pick(speciesPrototype.Sexes);
                age = random.Next(speciesPrototype.MinAge, speciesPrototype.OldAge); // people don't look and keep making 119 year old characters with zero rp, cap it at middle aged
            }

            // Corvax-TTS-Start
            var voiceId = random.Pick(prototypeManager
                .EnumeratePrototypes<TTSVoicePrototype>()
                .Where(x=>x.RoundStart)
                .Where(o => CanHaveVoice(o, sex)).ToArray()
            ).ID;
            // Corvax-TTS-End

            var gender = Gender.Epicene;

            switch (sex)
            {
                case Sex.Male:
                    gender = Gender.Male;
                    break;
                case Sex.Female:
                    gender = Gender.Female;
                    break;
            }

            var name = GetName(species, gender);

            return new HumanoidCharacterProfile()
            {
                Name = name,
                Sex = sex,
                Voice = voiceId,
                Age = age,
                Gender = gender,
                Species = species,
                Voice = voiceId, // Corvax-TTS
                Appearance = HumanoidCharacterAppearance.Random(species, sex),
            };
        }

        public HumanoidCharacterProfile WithName(string name)
        {
            return new(this) { Name = name };
        }

        public HumanoidCharacterProfile WithFlavorText(string flavorText)
        {
            return new(this) { FlavorText = flavorText };
        }

        public HumanoidCharacterProfile WithAge(int age)
        {
            return new(this) { Age = age };
        }

        public HumanoidCharacterProfile WithSex(Sex sex)
        {
            return new(this) { Sex = sex };
        }

        public HumanoidCharacterProfile WithGender(Gender gender)
        {
            return new(this) { Gender = gender };
        }

        public HumanoidCharacterProfile WithSpecies(string species)
        {
            return new(this) { Species = species };
        }

        // Corvax-TTS-Start
        public HumanoidCharacterProfile WithVoice(string voice)
        {
            return new(this) { Voice = voice };
        }
        // Corvax-TTS-End

        public HumanoidCharacterProfile WithCharacterAppearance(HumanoidCharacterAppearance appearance)
        {
            return new(this) { Appearance = appearance };
        }

        public HumanoidCharacterProfile WithSpawnPriorityPreference(SpawnPriorityPreference spawnPriority)
        {
            return new(this) { SpawnPriority = spawnPriority };
        }

        public HumanoidCharacterProfile WithJobPriorities(IEnumerable<KeyValuePair<string, JobPriority>> jobPriorities)
        {
            return new(this)
            {
                _jobPriorities = new Dictionary<string, JobPriority>(jobPriorities),
            };
        }

        public HumanoidCharacterProfile WithJobPriority(string jobId, JobPriority priority)
        {
            var dictionary = new Dictionary<string, JobPriority>(_jobPriorities);
            if (priority == JobPriority.Never)
            {
                dictionary.Remove(jobId);
            }
            else
            {
                dictionary[jobId] = priority;
            }

            return new(this)
            {
                _jobPriorities = dictionary,
            };
        }

        public HumanoidCharacterProfile WithPreferenceUnavailable(PreferenceUnavailableMode mode)
        {
            return new(this) { PreferenceUnavailable = mode };
        }

        public HumanoidCharacterProfile WithAntagPreferences(IEnumerable<string> antagPreferences)
        {
            return new(this)
            {
                _antagPreferences = new HashSet<string>(antagPreferences),
            };
        }

        public HumanoidCharacterProfile WithAntagPreference(string antagId, bool pref)
        {
            var list = new HashSet<string>(_antagPreferences);
            if (pref)
            {
                list.Add(antagId);
            }
            else
            {
                list.Remove(antagId);
            }

            return new(this)
            {
                _antagPreferences = list,
            };
        }

        public HumanoidCharacterProfile WithTraitPreference(string traitId, bool pref)
        {
            var list = new HashSet<string>(_traitPreferences);

            if (pref)
            {
                list.Add(traitId);
            }
            else
            {
                list.Remove(traitId);
            }

            return new(this)
            {
                _traitPreferences = list,
            };
        }

        public string Summary =>
            Loc.GetString(
                "humanoid-character-profile-summary",
                ("name", Name),
                ("gender", Gender.ToString().ToLowerInvariant()),
                ("age", Age)
            );

        public bool MemberwiseEquals(ICharacterProfile maybeOther)
        {
            if (maybeOther is not HumanoidCharacterProfile other) return false;
            if (Name != other.Name) return false;
            if (Voice != other.Voice) return false;
            if (Age != other.Age) return false;
            if (Sex != other.Sex) return false;
            if (Gender != other.Gender) return false;
            if (Species != other.Species) return false;
            if (PreferenceUnavailable != other.PreferenceUnavailable) return false;
            if (SpawnPriority != other.SpawnPriority) return false;
            if (!_jobPriorities.SequenceEqual(other._jobPriorities)) return false;
            if (!_antagPreferences.SequenceEqual(other._antagPreferences)) return false;
            if (!_traitPreferences.SequenceEqual(other._traitPreferences)) return false;
            if (!Loadouts.SequenceEqual(other.Loadouts)) return false;
            return Appearance.MemberwiseEquals(other.Appearance);
        }

        public void EnsureValid(ICommonSession session, IDependencyCollection collection, string[] sponsorPrototypes)
        {
            var configManager = collection.Resolve<IConfigurationManager>();
            var prototypeManager = collection.Resolve<IPrototypeManager>();

            if (!prototypeManager.TryIndex<SpeciesPrototype>(Species, out var speciesPrototype) || speciesPrototype.RoundStart == false)
            {
                Species = SharedHumanoidAppearanceSystem.DefaultSpecies;
                speciesPrototype = prototypeManager.Index<SpeciesPrototype>(Species);
            }

            // Corvax-Sponsors-Start: Reset to human if player not sponsor
            if (speciesPrototype.SponsorOnly && !sponsorPrototypes.Contains(Species))
            {
                Species = SharedHumanoidAppearanceSystem.DefaultSpecies;
                speciesPrototype = prototypeManager.Index<SpeciesPrototype>(Species);
            }
            // Corvax-Sponsors-End

            var sex = Sex switch
            {
                Sex.Male => Sex.Male,
                Sex.Female => Sex.Female,
                Sex.Unsexed => Sex.Unsexed,
                _ => Sex.Male // Invalid enum values.
            };

            // ensure the species can be that sex and their age fits the founds
            if (!speciesPrototype.Sexes.Contains(sex))
                sex = speciesPrototype.Sexes[0];

            var age = Math.Clamp(Age, speciesPrototype.MinAge, speciesPrototype.MaxAge);

            var gender = Gender switch
            {
                Gender.Epicene => Gender.Epicene,
                Gender.Female => Gender.Female,
                Gender.Male => Gender.Male,
                Gender.Neuter => Gender.Neuter,
                _ => Gender.Epicene // Invalid enum values.
            };

            string name;
            if (string.IsNullOrEmpty(Name))
            {
                name = GetName(Species, gender);
            }
            else if (Name.Length > MaxNameLength)
            {
                name = Name[..MaxNameLength];
            }
            else
            {
                name = Name;
            }

            name = name.Trim();

            if (configManager.GetCVar(CCVars.RestrictedNames))
            {
                name = RestrictedNameRegex.Replace(name, string.Empty);
            }

            if (configManager.GetCVar(CCVars.ICNameCase))
            {
                // This regex replaces the first character of the first and last words of the name with their uppercase version
                name = ICNameCaseRegex.Replace(name, m => m.Groups["word"].Value.ToUpper());
            }

            if (string.IsNullOrEmpty(name))
            {
                name = GetName(Species, gender);
            }

            string flavortext;
            if (FlavorText.Length > MaxDescLength)
            {
                flavortext = FormattedMessage.RemoveMarkup(FlavorText)[..MaxDescLength];
            }
            else
            {
                flavortext = FormattedMessage.RemoveMarkup(FlavorText);
            }

            var appearance = HumanoidCharacterAppearance.EnsureValid(Appearance, Species, Sex, sponsorPrototypes);

            var prefsUnavailableMode = PreferenceUnavailable switch
            {
                PreferenceUnavailableMode.StayInLobby => PreferenceUnavailableMode.StayInLobby,
                PreferenceUnavailableMode.SpawnAsOverflow => PreferenceUnavailableMode.SpawnAsOverflow,
                _ => PreferenceUnavailableMode.StayInLobby // Invalid enum values.
            };

            var spawnPriority = SpawnPriority switch
            {
                SpawnPriorityPreference.None => SpawnPriorityPreference.None,
                SpawnPriorityPreference.Arrivals => SpawnPriorityPreference.Arrivals,
                SpawnPriorityPreference.Cryosleep => SpawnPriorityPreference.Cryosleep,
                _ => SpawnPriorityPreference.None // Invalid enum values.
            };

            var priorities = new Dictionary<string, JobPriority>(JobPriorities
                .Where(p => prototypeManager.TryIndex<JobPrototype>(p.Key, out var job) && job.SetPreference && p.Value switch
                {
                    JobPriority.Never => false, // Drop never since that's assumed default.
                    JobPriority.Low => true,
                    JobPriority.Medium => true,
                    JobPriority.High => true,
                    _ => false
                }));

            var antags = AntagPreferences
                .Where(id => prototypeManager.TryIndex<AntagPrototype>(id, out var antag) && antag.SetPreference)
                .ToList();

            var traits = TraitPreferences
                         .Where(prototypeManager.HasIndex<TraitPrototype>)
                         .ToList();

            Name = name;
            FlavorText = flavortext;
            Age = age;
            Sex = sex;
            Gender = gender;
            Appearance = appearance;
            SpawnPriority = spawnPriority;

            _jobPriorities.Clear();

            foreach (var (job, priority) in priorities)
            {
                _jobPriorities.Add(job, priority);
            }

            PreferenceUnavailable = prefsUnavailableMode;

            _antagPreferences.Clear();
            _antagPreferences.UnionWith(antags);

            _traitPreferences.Clear();
            _traitPreferences.UnionWith(traits);

            // Corvax-TTS-Start
            prototypeManager.TryIndex<TTSVoicePrototype>(Voice, out var voice);
            if (voice is null || !CanHaveVoice(voice, Sex))
                Voice = SharedHumanoidAppearanceSystem.DefaultSexVoice[sex];
            // Corvax-TTS-End

            // Checks prototypes exist for all loadouts and dump / set to default if not.
            var toRemove = new ValueList<string>();

            foreach (var (roleName, loadouts) in _loadouts)
            {
                if (!prototypeManager.HasIndex<RoleLoadoutPrototype>(roleName))
                {
                    toRemove.Add(roleName);
                    continue;
                }

                loadouts.EnsureValid(this, session, collection);
            }

            foreach (var value in toRemove)
            {
                _loadouts.Remove(value);
            }
        }

        // Corvax-TTS-Start
        // SHOULD BE NOT PUBLIC, BUT....
        public static bool CanHaveVoice(TTSVoicePrototype voice, Sex sex)
        {
            return voice.RoundStart && sex == Sex.Unsexed || (voice.Sex == sex || voice.Sex == Sex.Unsexed);
        }
        // Corvax-TTS-End

        public ICharacterProfile Validated(ICommonSession session, IDependencyCollection collection, string[] sponsorPrototypes)
        {
            var profile = new HumanoidCharacterProfile(this);
            profile.EnsureValid(session, collection, sponsorPrototypes);
            return profile;
        }

        // sorry this is kind of weird and duplicated,
        /// working inside these non entity systems is a bit wack
        public static string GetName(string species, Gender gender)
        {
            var namingSystem = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<NamingSystem>();
            return namingSystem.GetName(species, gender);
        }

        public override bool Equals(object? obj)
        {
            return ReferenceEquals(this, obj) || (obj is HumanoidCharacterProfile other && MemberwiseEquals(other));
        }

        public override int GetHashCode()
        {
            var hashCode = new HashCode();
            hashCode.Add(_jobPriorities);
            hashCode.Add(_antagPreferences);
            hashCode.Add(_traitPreferences);
            hashCode.Add(_loadouts);
            hashCode.Add(Name);
            hashCode.Add(FlavorText);
            hashCode.Add(Species);
            hashCode.Add(Voice);
            hashCode.Add(Age);
            hashCode.Add((int)Sex);
            hashCode.Add((int)Gender);
            hashCode.Add(Appearance);
            hashCode.Add((int)SpawnPriority);
            hashCode.Add((int)PreferenceUnavailable);
            return hashCode.ToHashCode();
        }

        public void SetLoadout(RoleLoadout loadout)
        {
            _loadouts[loadout.Role.Id] = loadout;
        }

        public HumanoidCharacterProfile WithLoadout(RoleLoadout loadout)
        {
            // Deep copies so we don't modify the DB profile.
            var copied = new Dictionary<string, RoleLoadout>();

            foreach (var proto in _loadouts)
            {
                if (proto.Key == loadout.Role)
                    continue;

                copied[proto.Key] = proto.Value.Clone();
            }

            copied[loadout.Role] = loadout.Clone();
            var profile = Clone();
            profile._loadouts = copied;
            return profile;
        }

        public RoleLoadout GetLoadoutOrDefault(string id, ProtoId<SpeciesPrototype>? species, IEntityManager entManager, IPrototypeManager protoManager)
        {
            if (!_loadouts.TryGetValue(id, out var loadout))
            {
                loadout = new RoleLoadout(id);
                loadout.SetDefault(protoManager, force: true);
            }

            loadout.SetDefault(protoManager);
            return loadout;
        }

        public HumanoidCharacterProfile Clone()
        {
            return new HumanoidCharacterProfile(this);
        }
    }
}
