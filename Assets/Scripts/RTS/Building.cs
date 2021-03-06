﻿using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshObstacle))]
public class Building : ClickableObject
{

    public enum BuildingStates
    {
        Idleing,
        Attacking,
        Dead,
    }

    public BuildingStates state = BuildingStates.Idleing;

    //references
    protected NavMeshObstacle navMeshObstacle;
    protected ParticleSystem[] burnEffects;

    protected override void Awake()
    {
        base.Awake();
        navMeshObstacle = GetComponent<NavMeshObstacle>();
        fieldOfView = GetComponentInChildren<FieldOfView>();
        burnEffects = transform.Find("BurnPoints").GetChildren().Select(child => child.GetComponent<ParticleSystem>()).ToArray();
    }

    protected override void Start()
    {
        faction.buildings.Add(this);

        SetColorMaterial();

        //Set some defaults, including the default state
        SetSelected(false);

        base.Start();
    }

    public new static bool IsDeadOrNull(ClickableObject unit)
    {
        return unit == null || ((unit is Building) ? (unit as Building).state == BuildingStates.Dead : ClickableObject.IsDeadOrNull(unit));
    }

    protected IEnumerator DecayIntoGround()
    {
        float startY = transform.position.y;
        float depth = 5f;
        while (transform.position.y > startY - depth)
        {
            transform.position += Vector3.down * Time.deltaTime * 0.1f;
            yield return null;
        }
        Destroy(gameObject);
    }

    public override void SetVisibility(bool visibility, bool force = false)
    {
        if (!force && visibility == visible)
        {
            return;
        }

        base.SetVisibility(visibility, force);

        if (visible)
        {
            UIManager.Instance.AddHealthbar(this);
        }
        else
        {
            if (OnDisapearInFOW != null)
            {
                OnDisapearInFOW.Invoke(this);
            }
        }
    }

    //called by an attacker
    public override void SufferAttack(int damage)
    {
        if (state == BuildingStates.Dead)
        {
            return;
        }

        base.SufferAttack(damage);

        TriggerBurnEffects();
    }

    protected void TriggerBurnEffects()
    {
        float healthPerc = template.health / (float)template.original.health;

        if (healthPerc <= 0f || state == BuildingStates.Dead)
        {
            foreach (var effect in burnEffects)
            {
                effect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }
        else if (burnEffects.Length == 4)
        {
            bool[] shouldBurn = new bool[4];
            shouldBurn[0] = healthPerc < 0.75f;
            shouldBurn[1] = healthPerc < 0.5f;
            shouldBurn[2] = healthPerc < 0.25f;
            shouldBurn[3] = healthPerc < 0.25f;

            for (int i = 0; i < 4; i++)
            {
                if (shouldBurn[i] != burnEffects[i].isPlaying)
                {
                    if (shouldBurn[i])
                    {
                        burnEffects[i].Play(true);
                    }
                    else
                    {
                        burnEffects[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    }
                }
            }
        }
    }

    protected override void Die()
    {
        base.Die();

        state = BuildingStates.Dead; //still makes sense to set it, because somebody might be interacting with this script before it is destroyed

        SetSelected(false);

        TriggerBurnEffects();

        faction.buildings.Remove(this);

        //Remove unneeded Components
        StartCoroutine(HideSeenThings(visionFadeTime));
        StartCoroutine(VisionFade(visionFadeTime, true));
        navMeshObstacle.enabled = false;
        StartCoroutine(DecayIntoGround());
    }
}
