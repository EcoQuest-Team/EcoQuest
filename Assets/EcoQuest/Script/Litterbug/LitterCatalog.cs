using System;
using System.Collections.Generic;
using UnityEngine;

public enum LitterCategory
{
    Biodegradable = 0,
    Recyclable = 1,
    NonBiodegradable = 2,
    Hazardous = 3,
    EWasteAndMedical = 4
}

[Serializable]
public class LitterTypeEntry
{
    public int id;
    public string displayName;
    public LitterCategory category;
    public LitterProp propPrefab;
    public bool shattersOnLanding;
    [TextArea(2, 4)]
    public string[] dialogueLines = new string[5]; // NPC, YOU, NPC, YOU, NPC
}

[CreateAssetMenu(fileName = "LitterCatalog", menuName = "EcoQuest/Litterbug Catalog")]
public class LitterCatalog : ScriptableObject
{
    public static readonly string[] FiveLineSpeakers = { "NPC", "YOU", "NPC", "YOU", "NPC" };

    public List<LitterTypeEntry> entries = new List<LitterTypeEntry>(15);

    public void PopulateDefaultEntries()
    {
        entries.Clear();

        // BIODEGRADABLE (1..3)
        Add(1, "Banana peel", LitterCategory.Biodegradable, false,
            "Relax, it's just a banana peel. It's natural, right?",
            "Natural, yes, but not in the street. It takes weeks to rot, and meanwhile it draws flies and rats.",
            "Hmm... I didn't think about pests.",
            "It's also a slipping hazard. In the right bin, it can become compost for plants.",
            "Okay, okay. Biodegradable bin from now on!");

        Add(2, "Apple core", LitterCategory.Biodegradable, false,
            "It's an apple core. Birds and bugs will finish it.",
            "Food scraps on the street attract rats and flies, and the smell spreads through the whole block.",
            "Ugh, so that's why it smells near the bench.",
            "Food buried in landfills also makes methane, a gas that warms the planet. Composting avoids that.",
            "I'll put it in the biodegradable bin. Thanks for explaining!");

        Add(3, "Leftover food", LitterCategory.Biodegradable, false,
            "It's just leftover rice. Why does it matter?",
            "Rotting food draws rats and flies, and they carry germs that can make people sick.",
            "Sick? From one little pile?",
            "Pests spread whatever they touch. Scraps belong in the biodegradable bin or a compost pile.",
            "Alright, I'll clean it up and sort it properly.");

        // RECYCLABLE (4..6)
        Add(4, "Plastic bottle", LitterCategory.Recyclable, false,
            "One bottle won't change anything.",
            "If everyone thinks that, bottles pile up, block the drains, and the street floods when it rains.",
            "Oh... so that's why this street floods.",
            "A recycled bottle can become a new bottle, or even fabric for clothes and bags.",
            "Wow! I'll rinse it and use the recyclable bin.");

        Add(5, "Soda can", LitterCategory.Recyclable, false,
            "It's empty, so it's trash now.",
            "It's a resource! Aluminum can be recycled again and again, using up to 95% less energy than making new.",
            "Really? I thought cans were junk.",
            "Tossed on the street, that metal and energy are wasted and the can becomes litter.",
            "Got it. Into the recyclable bin it goes!");

        Add(6, "Cardboard", LitterCategory.Recyclable, false,
            "It's just paper. The rain will wash it away.",
            "Wet cardboard turns to mush and clogs drains. Recycling paper also saves trees and water.",
            "I didn't know it could clog drains.",
            "Keep it flat, clean, and dry, and it can become new boxes.",
            "Flat, clean, dry. I'll use the recyclable bin.");

        // NON-BIODEGRADABLE (7..9)
        Add(7, "Plastic bag", LitterCategory.NonBiodegradable, false,
            "It's a light bag. The wind will take it somewhere.",
            "Somewhere is the problem. Plastic bags can last for hundreds of years, and sea turtles mistake them for jellyfish.",
            "That's so sad. I never thought about animals.",
            "One reusable bag can replace many single-use ones.",
            "I'll use the non-biodegradable bin and bring my own bag.");

        Add(8, "Chip wrapper", LitterCategory.NonBiodegradable, false,
            "It's a tiny wrapper. Nobody will notice.",
            "Wrappers don't rot. They break into tiny plastic bits that wash into rivers and the sea.",
            "So my snack wrapper could end up in the ocean?",
            "It can. Fish and other animals can swallow those bits, and that can reach our food.",
            "Not cool. I'll hold on to it until I find the non-biodegradable bin.");

        Add(9, "Foam cup", LitterCategory.NonBiodegradable, false,
            "It's a foam cup. It's basically air.",
            "Foam never really breaks down. It crumbles into smaller pieces that animals mistake for food.",
            "I didn't know it never goes away.",
            "A reusable cup keeps lots of cups out of the trash.",
            "Good idea. Non-biodegradable bin now, and my own cup next time.");

        // HAZARDOUS (10..12)
        Add(10, "Battery", LitterCategory.Hazardous, false,
            "It's a dead battery. It's just junk.",
            "Not quite. Batteries hold metals and chemicals that leak into soil and water.",
            "Leak? Into the water we use?",
            "Lithium batteries can even catch fire if crushed. They need a proper drop-off point.",
            "Whoa. I'll take it to a drop-off point right away.");

        Add(11, "Spray can", LitterCategory.Hazardous, false,
            "It's empty, I used it all up.",
            "Even empty cans hold leftover chemicals and pressure. They can leak into the ground or burst in the heat.",
            "Burst? I had no idea.",
            "Never throw them in regular trash or burn them. Hazardous waste collection handles them safely.",
            "Okay, I'll bring it to a hazardous waste collection.");

        Add(12, "Broken glass", LitterCategory.Hazardous, true,
            "It's just a broken bottle. I'm in a hurry.",
            "Sharp glass can cut people, animals, and the workers who collect the trash.",
            "Oh no, I didn't think about the collectors.",
            "Wrap it in thick paper or cloth and label it so people know it's sharp.",
            "Got it. I'll wrap it up and put it in the right bin.");

        // E-WASTE & MEDICAL (13..15)
        Add(13, "Old phone", LitterCategory.EWasteAndMedical, false,
            "It's a broken phone. It's useless now.",
            "Not useless. Phones contain metals like copper and gold, and also toxic materials.",
            "Gold? In a phone?",
            "Tiny amounts, and recovering them reduces mining. Dumped, the toxins seep into soil and water.",
            "I'll take it to an e-waste collection point.");

        Add(14, "Tangled charger", LitterCategory.EWasteAndMedical, false,
            "It's a tangled old charger. Just cables.",
            "Cables hold copper that can be recovered and reused. The plastic coating pollutes when dumped or burned.",
            "So the copper can be saved?",
            "Yes. E-waste centers recover the metal and handle the rest safely.",
            "I'll bring it to an e-waste drop-off. Thanks!");

        Add(15, "Used face mask", LitterCategory.EWasteAndMedical, false,
            "It's just a used mask.",
            "Masks are made of plastic fibers and can carry germs. The ear loops also tangle birds and other animals.",
            "Tangle animals? I never thought of that.",
            "Cut the ear loops, seal the mask in a bag, then use the non-biodegradable bin.",
            "Loops cut, bag sealed. Will do!");
    }

    private void Add(int id, string name, LitterCategory cat, bool shatters, string l0, string l1, string l2, string l3, string l4)
    {
        entries.Add(new LitterTypeEntry
        {
            id = id,
            displayName = name,
            category = cat,
            shattersOnLanding = shatters,
            dialogueLines = new[] { l0, l1, l2, l3, l4 }
        });
    }
}
