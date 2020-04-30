﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "new Faction Template", menuName = "RTS/Faction Template", order = 2)]
public class FactionTemplate : ScriptableObject
{
    public enum FactionColor
    {
        black,
        blue,
        brown,
        green,
        purple,
        red,
        orange,
        white,
    }
    public enum Race
    {
        Human,
        Orc,
    }

    public FactionColor factionColorName = FactionColor.black;
    public Color color = Color.black;

    [Space]

    public Material humanUnitsMaterial;
    public Material orcUnitsMaterial;

    [Space]

    public List<FactionTemplate> allies = new List<FactionTemplate>();

    public List<Unit> units { get; private set; }

    void OnEnable()
    {
        units = new List<Unit>();
    }

    void OnDisable()
    {
        units = new List<Unit>();
    }

    public Material GetMaterial(Race race)
    {
        switch (race)
        {
            case Race.Human:
                return humanUnitsMaterial;
            case Race.Orc:
                return orcUnitsMaterial;
            default:
                Debug.LogError("Race doesn't have a Unit material for this faction color", this);
                return null;
        }
    }
}
