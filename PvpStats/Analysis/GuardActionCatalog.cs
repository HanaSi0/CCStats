using System.Collections.Generic;
namespace PvpStats.Analysis;
// Official PvP descriptions captured 2026-10-17. Scope is a conditional Guard-only counterfactual.
public static class GuardActionCatalog {
    public static readonly HashSet<uint> Direct = new() {
        29058, // Fast Blade
        29059, // Riot Blade
        29060, // Royal Authority
        29061, // Atonement
        29062, // Holy Spirit
        29065, // Intervene
        29067, // Holy Sheltron
        29068, // Holy Sheltron
        29071, // Blade of Faith
        29072, // Blade of Truth
        29073, // Blade of Valor
        29074, // Heavy Swing
        29075, // Maim
        29076, // Storm's Path
        29077, // Inner Chaos
        29078, // Fell Cleave
        29084, // Primal Rend
        29085, // Hard Slash
        29086, // Syphon Strike
        29087, // Souleater
        29092, // Plunge
        29098, // Keen Edge
        29099, // Brutal Shell
        29100, // Solid Barrel
        29101, // Burst Strike
        29102, // Gnashing Fang
        29103, // Savage Claw
        29104, // Wicked Talon
        29107, // Hypervelocity
        29108, // Jugular Rip
        29109, // Abdomen Tear
        29110, // Eye Gouge
        29123, // Rough Divide
        29128, // Blasting Zone
        29131, // Terminal Trigger
        29223, // Glare III
        29226, // Afflatus Misery
        29229, // Seraph Strike
        29230, // Afflatus Purgation
        29231, // Broil IV
        29242, // Fall Malefic
        29244, // Gravity II
        29246, // Fall Malefic
        29248, // Gravity II
        29253, // Macrocosmos
        29256, // Dosis III
        29257, // Eukrasian Dosis III
        29259, // Phlegma III
        29260, // Pneuma
        29262, // Toxikon
        29263, // Toxikon II
        29391, // Powerful Shot
        29392, // Pitch Perfect
        29393, // Apex Arrow
        29394, // Blast Arrow
        29399, // Repelling Shot
        29402, // Blast Charge
        29404, // Scattergun
        29406, // Bioblaster
        29407, // Air Anchor
        29408, // Chain Saw
        29409, // Wildfire
        29410, // Wildfire
        29411, // Wildfire
        29412, // Bishop Autoturret
        29413, // Aether Mortar
        29415, // Marksman's Spite
        29416, // Cascade
        29417, // Fountain
        29418, // Reverse Cascade
        29419, // Fountainfall
        29420, // Saber Dance
        29421, // Starfall Dance
        29423, // Honing Ovation
        29424, // Honing Ovation
        29425, // Honing Ovation
        29426, // Honing Ovation
        29427, // Honing Ovation
        29428, // Fan Dance
        29469, // Terminal Trigger
        29470, // Honing Ovation
        29475, // Dragon Kick
        29476, // Twin Snakes
        29477, // Demolish
        29478, // Phantom Rush
        29481, // Rising Phoenix
        29482, // Riddle of Earth
        29483, // Earth's Reply
        29486, // Raiden Thrust
        29487, // Fang and Claw
        29488, // Wheeling Thrust
        29489, // Heavens' Thrust
        29491, // Geirskogul
        29492, // Nastrond
        29493, // High Jump
        29495, // Wyrmwind Thrust
        29496, // Horrid Roar
        29498, // Sky Shatter
        29499, // Sky Shatter
        29500, // Spinning Edge
        29501, // Gust Slash
        29502, // Aeolian Edge
        29504, // Goka Mekkyaku
        29505, // Fuma Shuriken
        29506, // Hyosho Ranryu
        29510, // Forked Raiju
        29517, // Spinning Edge
        29518, // Gust Slash
        29519, // Aeolian Edge
        29521, // Fuma Shuriken
        29522, // Forked Raiju
        29523, // Yukikaze
        29524, // Gekko
        29525, // Kasha
        29526, // Hyosetsu
        29527, // Mangetsu
        29528, // Oka
        29530, // Ogi Namikiri
        29531, // Kaeshi: Namikiri
        29533, // Hissatsu: Chiten
        29535, // Mineuchi
        29538, // Slice
        29539, // Waxing Slice
        29540, // Infernal Slice
        29543, // Void Reaping
        29544, // Cross Reaping
        29545, // Harvest Moon
        29546, // Plentiful Harvest
        29547, // Grim Swathe
        29548, // Lemure's Slice
        29549, // Death Warrant
        29554, // Communio
        29603, // Death Warrant
        29649, // Fire
        29650, // Fire IV
        29651, // Flare
        29653, // Blizzard
        29654, // Blizzard IV
        29655, // Freeze
        29657, // Burst
        29658, // Xenoglossy
        29663, // Paradox
        29664, // Ruin III
        29665, // Astral Impulse
        29666, // Fountain of Fire
        29667, // Crimson Cyclone
        29668, // Crimson Strike
        29669, // Slipstream
        29671, // Mountain Buster
        29675, // Megaflare
        29676, // Wyrmwave
        29681, // Scarlet Flame
        29699, // Corps-a-corps
        29700, // Displacement
        29706, // Pneuma
        29707, // Fleeting Raiju
        29708, // Fleeting Raiju
        29736, // Chaotic Cyclone
        30896, // Fire III
        30897, // Blizzard III
        39157, // Steel Fangs
        39158, // Piercing Fangs
        39159, // Hunter's Sting
        39160, // Swiftskin's Sting
        39161, // Barbarous Bite
        39163, // Ravenous Bite
        39166, // Bloodcoil
        39167, // Sanguine Feast
        39168, // Uncoiled Fury
        39169, // First Generation
        39170, // Second Generation
        39171, // Third Generation
        39172, // Fourth Generation
        39173, // Ouroboros
        39185, // Snake Scales
        39186, // Backlash
        39187, // Backlash
        39190, // World-swallower
        39198, // Holy in White
        39199, // Comet in Black
        39216, // Star Prism
        39217, // Star Prism
        41428, // Supplication
        41429, // Sepulchre
        41431, // Imperator
        41432, // Primal Ruination
        41433, // Primal Wrath
        41434, // Scarlet Delirium
        41435, // Comeuppance
        41436, // Torcleaver
        41437, // Disesteem
        41438, // Impalement
        41442, // Fated Brand
        41444, // Leaping Opo
        41445, // Rising Raptor
        41446, // Pouncing Coeurl
        41448, // Fire's Reply
        41449, // Drakesbane
        41450, // Starcross
        41451, // Dokumori
        41452, // Zesho Meppo
        41453, // Zesho Meppo
        41454, // Tendo Setsugekka
        41455, // Tendo Kaeshi Setsugekka
        41456, // Executioner's Guillotine
        41458, // Perfectio
        41464, // Harmonic Arrow
        41465, // Harmonic Arrow
        41466, // Harmonic Arrow
        41467, // Encore of Light
        41468, // Blazing Shot
        41469, // Full Metal Field
        41472, // Dance of the Dawn
        41473, // High Fire II
        41474, // High Blizzard II
        41478, // Wreath of Ice
        41480, // Flare Star
        41481, // Frost Star
        41482, // Ruin IV
        41483, // Necrotize
        41484, // Deathflare
        41485, // Brand of Purgatory
        41486, // Jolt III
        41487, // Grand Impact
        41491, // Scorch
        41492, // Resolution
        41493, // Vice of Thorns
        41495, // Prefulgence
        41499, // Glare IV
        41500, // Seraphic Halo
        41505, // Lord of Crowns
        41508, // Oracle
        41509, // Wind's Reply
        41511, // Fated Circle
        41577, // Zanshin
        41658, // Psyche
        41964, // Harmonic Arrow
        42194, // Confiteor
    };
    public static readonly HashSet<uint> Drain = new() { 29078,29490,29543,29544,29545,29658,39166,39167,39173,39186,39187,39190,41438,41577 };
}
