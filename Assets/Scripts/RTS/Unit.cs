﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// <summary>
/// Unit semi-AI handles movement and stats
/// </summary>
public class Unit : MonoBehaviour
{
    public enum UnitStates
    {
        Idleing,
        Guarding,
        Attacking,
        MovingToTarget,
        MovingToSpot,
        AttackMovingToSpot,
        Dead,
    }

    public static List<Unit> globalUnitsList;
    private static int layerDefaultVisible;
    private static int layerDefaultHidden;
    private static int layerMiniMapVisible;
    private static int layerMiniMapHidden;

    public UnitStates state = UnitStates.Idleing;
    public FactionTemplate faction;
    public bool visible;
    public float visionFadeTime = 1f;
    public float combatReadySwitchTime = 7f;
    [Preview]
    public UnitTemplate template;
    public Transform projectileFirePoint;

    //references
    private NavMeshAgent navMeshAgent;
    private Animator animator;
    private MeshRenderer selectionCircle, miniMapCircle, visionCircle;
    private FieldOfView fieldOfView;
    private Transform modelHolder;
    private Renderer[] modelRenderers;

    //private bool isSelected; //is the Unit currently selected by the Player
    [HideInInspector]
    public List<AICommand> commandList = new List<AICommand>();
    private bool commandRecieved, commandExecuted;
    private Unit targetOfAttack;
    private Unit[] hostiles;
    private float lastGuardCheckTime, guardCheckInterval = 1f;
    private bool agentReady = false;
    public UnityAction<Unit> OnDeath;
    public UnityAction<Unit> OnDisapearInFOW;
    private Coroutine LerpingCombatReady;

    static Unit()
    {
        globalUnitsList = new List<Unit>();
    }

    void Awake()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        selectionCircle = transform.Find("SelectionCircle").GetComponent<MeshRenderer>();
        miniMapCircle = transform.Find("MiniMapCircle").GetComponent<MeshRenderer>();
        visionCircle = transform.Find("FieldOfView").GetComponent<MeshRenderer>();
        fieldOfView = transform.Find("FieldOfView").GetComponent<FieldOfView>();
        modelHolder = transform.Find("Model");
        modelRenderers = modelHolder.GetComponentsInChildren<Renderer>(true);

        SetLayers();
    }

    void Start()
    {
        //Randomization of NavMeshAgent speed. More fun!
        //float rndmFactor = navMeshAgent.speed * .15f;
        //navMeshAgent.speed += Random.Range(-rndmFactor, rndmFactor);

        template = template.Clone(); //we copy the template otherwise it's going to overwrite the original asset!

        globalUnitsList.Add(this);
        faction.units.Add(this);

        SetColorMaterial();

        //Set some defaults, including the default state
        SetSelected(false);

        StartCoroutine(DequeueCommands());

        visionCircle.material.color = visionCircle.material.color.ToWithA(0f);
        if (FactionTemplate.IsAlliedWith(faction, GameManager.Instance.playerFaction))
        {
            StartCoroutine(VisionFade(visionFadeTime, false));
            SetVisibility(true);
        }
        else
        {
            fieldOfView.enabled = false;
            visible = true;
            SetVisibility(false);
        }
    }

    void Update()
    {
        //Little hack to give time to the NavMesh agent to set its destination.
        //without this, the Unit would switch its state before the NavMeshAgent can kick off, leading to unpredictable results
        if (!agentReady)
        {
            agentReady = true;
            return;
        }

        UpdateMinimapUI();

        switch (state)
        {
            case UnitStates.MovingToSpot:
                if (navMeshAgent.remainingDistance <= navMeshAgent.stoppingDistance)
                {
                    Idle();
                }
                AdjustModelAngleToGround();
                break;

            case UnitStates.AttackMovingToSpot:
                if (navMeshAgent.remainingDistance <= navMeshAgent.stoppingDistance)
                {
                    commandExecuted = true;
                    AddCommand(new AICommand(AICommand.CommandType.Guard));
                }
                else
                {
                    if (fieldOfView.lastVisibleTargets.Count > 0)
                    {
                        //var enemies = fieldOfView.lastVisibleTargets.Where(target => !IsDeadOrNull(target.GetComponent<Unit>()) && !FactionTemplate.IsAlliedWith(faction, target.GetComponent<Unit>().faction));
                        var enemies = fieldOfView.lastVisibleTargets.Where(target => !FactionTemplate.IsAlliedWith(target.GetComponent<Unit>().faction, faction) && target.GetComponent<Unit>().state != UnitStates.Dead);
                        if (enemies.Count() > 0)
                        {
                            var closestEnemy = enemies.FindClosestToPoint(transform.position).GetComponent<Unit>();
                            //MoveToAttack(closestEnemy);
                            commandExecuted = true;
                            commandRecieved = false;
                            InsertCommand(new AICommand(AICommand.CommandType.AttackTarget, closestEnemy));
                        }
                    }
                }
                AdjustModelAngleToGround();
                break;

            case UnitStates.MovingToTarget:
                //check if target has been killed by somebody else
                if (IsDeadOrNull(targetOfAttack))
                {
                    commandExecuted = true;
                    //Idle();
                }
                else
                {
                    if (commandList.Count >= 2 && commandList[1].commandType == AICommand.CommandType.Guard)
                    {
                        if (Vector3.Distance(commandList[1].destination, transform.position) > template.guardDistance * 2f)
                        {
                            commandExecuted = true;
                            InsertCommand(new AICommand(AICommand.CommandType.MoveTo, commandList[1].destination), 1);
                        }
                    }
                    //Check for distance from target
                    if (navMeshAgent.remainingDistance < template.engageDistance)
                    {
                        navMeshAgent.velocity = Vector3.zero;
                        StartAttacking();
                    }
                    else
                    {
                        navMeshAgent.SetDestination(targetOfAttack.transform.position); //update target position in case it's moving
                    }
                }
                AdjustModelAngleToGround();
                break;

            case UnitStates.Guarding:
                if (Time.time > lastGuardCheckTime + guardCheckInterval)
                {
                    lastGuardCheckTime = Time.time;
                    Unit[] closestEnemies = GetNearestHostileUnits();
                    for (int i = 0; i < closestEnemies.Length; i++)
                    {
                        commandExecuted = true;
                        commandRecieved = false;
                        InsertCommand(new AICommand(AICommand.CommandType.AttackTarget, closestEnemies[i]));
                    }
                    AdjustModelAngleToGround();
                }
                break;

            case UnitStates.Attacking:
                //check if target has been killed by somebody else
                commandExecuted = true;
                if (IsDeadOrNull(targetOfAttack))
                {
                    if (animator != null)
                    {
                        animator.SetBool("DoAttack", false);
                    }
                    Idle();
                }
                else if (commandList.Count >= 2 && commandList[1].commandType == AICommand.CommandType.Guard)
                {
                    if (Vector3.Distance(commandList[1].destination, transform.position) > 0.1f)
                    {
                        InsertCommand(new AICommand(AICommand.CommandType.MoveTo, commandList[1].destination), 1);
                    }
                }
                else if (Vector3.Distance(targetOfAttack.transform.position, transform.position) > template.engageDistance)
                {
                    //Check if the target moved away for some reason
                    if (animator != null)
                    {
                        animator.SetBool("DoAttack", false);
                    }

                    MoveToTarget(targetOfAttack);
                }
                else if (Vector3.Angle(transform.forward, (targetOfAttack.transform.position - transform.position).normalized) > 10f)
                {
                    //look towards the target
                    Vector3 desiredForward = (targetOfAttack.transform.position - transform.position).normalized;
                    transform.forward = Vector3.Lerp(transform.forward, desiredForward, Time.deltaTime * 10f);
                }
                else
                {
                    if (animator != null)
                    {
                        animator.SetBool("DoAttack", true);
                    }
                }
                break;
            case UnitStates.Dead:
                if (template.health != 0)
                {
                    Die();
                }
                return;
        }

        float navMeshAgentSpeed = navMeshAgent.velocity.magnitude;
        if (animator != null)
        {
            animator.SetFloat("Speed", navMeshAgentSpeed * .05f);
        }

        //float scalingCorrection = template.guardDistance * 2f * 1.05f;
        //if (visionCircle.transform.localScale.x != template.guardDistance * scalingCorrection)
        //{
        //    visionCircle.transform.localScale = Vector3.one * scalingCorrection;
        //}
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (navMeshAgent != null
            && navMeshAgent.isOnNavMesh
            && navMeshAgent.hasPath)
        {
            UnityEditor.Handles.color = Color.yellow;//Random.onUnitSphere.ToVector4(1f).ToColor();
            UnityEditor.Handles.DrawLine(transform.position, navMeshAgent.destination);
        }

        UnityEditor.Handles.color = Color.cyan;
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, template.engageDistance);
        UnityEditor.Handles.color = Color.gray;
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, template.guardDistance);
    }
#endif

    public static bool IsDeadOrNull(Unit unit)
    {
        return (unit == null || unit.state == UnitStates.Dead);
    }

    private static void SetLayers()
    {
        layerDefaultVisible = LayerMask.NameToLayer("Default");
        layerDefaultHidden = LayerMask.NameToLayer("Default Hidden");
        layerMiniMapVisible = LayerMask.NameToLayer("MiniMap Only");
        layerMiniMapHidden = LayerMask.NameToLayer("MiniMap Hidden");
    }

    private void SetColorMaterial()
    {
        foreach (Renderer render in modelRenderers)
        {
            if (render.materials.Length == 1)
            {
                render.material.SetColor("_TeamColor", faction.color);
            }
            else
            {
                render.materials[render.materials.Length - 1].SetColor("_TeamColor", faction.color);
            }
        }
    }

    public void AddCommand(AICommand command, bool clear = false)
    {
        if (!CheckCommandViability(command))
        {
            return;
        }
        if (clear || command.commandType == AICommand.CommandType.Stop)
        {
            commandList.Clear();
        }
        commandExecuted = true;
        commandRecieved = false;
        if (command.commandType != AICommand.CommandType.Stop)
        {
            commandList.Add(command);
        }
    }

    public void InsertCommand(AICommand command, int position = 0)
    {
        if (!CheckCommandViability(command))
        {
            return;
        }
        commandList.Insert(position, command);
    }

    private bool CheckCommandViability(AICommand command)
    {
        //make units be able to denie command... oh what could possibly go wrong
        switch (command.commandType)
        {
            case AICommand.CommandType.MoveTo:
            case AICommand.CommandType.AttackMoveTo:
            case AICommand.CommandType.Guard:
                return !command.destination.IsNaN();
            case AICommand.CommandType.AttackTarget:
                return !IsDeadOrNull(command.target) && command.target != this;
            case AICommand.CommandType.Stop:
            case AICommand.CommandType.Die:
                return true;
        }
        throw new System.NotImplementedException(string.Concat("Command Type '", command.commandType.ToString(), "' not valid"));
    }

    private IEnumerator DequeueCommands()
    {
        commandRecieved = false;
        commandExecuted = true;
        switch (state)
        {
            case UnitStates.Idleing:
                break;
            case UnitStates.Guarding:
                AddCommand(new AICommand(AICommand.CommandType.Guard, transform.position));
                break;
            default:
                Debug.LogError("Cannot start with a state different to Idle or Guard. State has been set to Idle.", gameObject);
                state = UnitStates.Idleing;
                goto case UnitStates.Idleing;
        }
        for (; ; )
        {
            if (state == UnitStates.Dead)
            {
                //already dead
                yield break;
            }
            if (commandList.Count == 0)
            {
                yield return null;
                continue;
            }
            else
            {
                if (commandExecuted)
                {
                    if (commandList.Count == 1 && (commandList[0].commandType == AICommand.CommandType.Guard))
                    {
                        yield return null;
                        continue;
                    }

                    if (commandRecieved)
                    {
                        commandList.RemoveAt(0);
                        commandRecieved = false;
                    }
                    commandExecuted = false;

                    if (commandList.Count == 0)
                    {
                        continue;
                    }

                    AICommand nextCommand = commandList[0];
                    ExecuteCommand(nextCommand);
                }
                yield return null;
            }
        }
    }

    private void ExecuteCommand(AICommand command)
    {
        if (state == UnitStates.Dead)
        {
            //already dead
            Debug.LogWarning("Unit is dead. Cannot execute command.", gameObject);
            return;
        }


        //Debug.Log(string.Concat(name, " Execute cmd: ", command.commandType));

        commandExecuted = false;
        commandRecieved = true;
        switch (command.commandType)
        {
            case AICommand.CommandType.MoveTo:
                MoveToSpot(command.destination);
                break;

            case AICommand.CommandType.AttackMoveTo:
                AttackMoveToSpot(command.destination);
                break;

            case AICommand.CommandType.Stop:
                Idle();
                break;

            case AICommand.CommandType.Guard:
                Guard();
                break;

            case AICommand.CommandType.AttackTarget:
                MoveToTarget(command.target);
                break;

            case AICommand.CommandType.Die:
                Die();
                break;
        }
    }

    //move to a position and be idle
    private void MoveToSpot(Vector3 location)
    {
        state = UnitStates.MovingToSpot;

        targetOfAttack = null;
        agentReady = false;

        navMeshAgent.isStopped = false;
        navMeshAgent.SetDestination(location);
    }

    //move to a position and be guarding
    private void AttackMoveToSpot(Vector3 location)
    {
        state = UnitStates.AttackMovingToSpot;

        targetOfAttack = null;
        agentReady = false;

        navMeshAgent.isStopped = false;
        navMeshAgent.SetDestination(location);
    }

    //stop and stay Idle
    private void Idle()
    {
        state = UnitStates.Idleing;
        commandExecuted = true;

        targetOfAttack = null;
        agentReady = false;

        navMeshAgent.isStopped = true;
        navMeshAgent.velocity = Vector3.zero;
    }

    //stop but watch for enemies nearby
    public void Guard()
    {
        state = UnitStates.Guarding;
        commandExecuted = true;

        targetOfAttack = null;
        agentReady = false;

        navMeshAgent.isStopped = true;
        navMeshAgent.velocity = Vector3.zero;
    }

    //move towards a target to attack it
    private void MoveToTarget(Unit target)
    {
        if (!IsDeadOrNull(target))
        {
            state = UnitStates.MovingToTarget;
            targetOfAttack = target;
            agentReady = false;

            if (navMeshAgent != null)
            {
                navMeshAgent.isStopped = false;
                navMeshAgent.SetDestination(target.transform.position);
            }
        }
        else
        {
            //if the command is dealt by a Timeline, the target might be already dead
            commandExecuted = true;
        }
    }

    //reached the target (within engageDistance), time to attack
    private void StartAttacking()
    {
        //somebody might have killed the target while this Unit was approaching it
        if (!IsDeadOrNull(targetOfAttack))
        {
            state = UnitStates.Attacking;
            agentReady = false;
            navMeshAgent.isStopped = true;
        }
        else
        {
            commandExecuted = true;
            //AddCommand(new AICommand(AICommand.CommandType.Stop));
        }
    }

    public void TriggerAttackAnimEvent(int Int)//Functionname equals Eventname
    {
        if (state == UnitStates.Dead || IsDeadOrNull(targetOfAttack))
        {
            //already dead
            animator.SetBool("DoAttack", false);
            return;
        }

        int damage = Random.Range(template.damage.x, template.damage.y + 1);
        if (template.projectile != null)
        {
            ShootProjectile(damage);
        }
        else
        {
            targetOfAttack.SufferAttack(damage);
        }
    }

    //called by an attacker
    public void SufferAttack(int damage)
    {
        if (state == UnitStates.Dead)
        {
            return;
        }

        template.health -= damage;

        if (template.health <= 0)
        {
            Die();
        }
    }

    //called in SufferAttack, but can also be from a Timeline clip
    [ContextMenu("Die")]
    private void Die()
    {
        if (Application.isEditor && !Application.isPlaying)
        {
            return;
        }
        AdjustModelAngleToGround();

        template.health = 0;

        commandList.Clear();
        commandExecuted = true;

        state = UnitStates.Dead; //still makes sense to set it, because somebody might be interacting with this script before it is destroyed
        if (animator != null)
        {
            animator.SetTrigger("DoDeath");
        }

        //Remove itself from the selection Platoon
        GameManager.Instance.RemoveFromSelection(this);
        SetSelected(false);

        //Fire an event so any Platoon containing this Unit will be notified
        if (OnDeath != null)
        {
            OnDeath.Invoke(this);
        }

        //To avoid the object participating in any Raycast or tag search
        //gameObject.tag = "Untagged";
        gameObject.layer = 0;

        globalUnitsList.Remove(this);
        faction.units.Remove(this);

        //Remove unneeded Components
        StartCoroutine(HideSeenThings(visionFadeTime / 2f));
        StartCoroutine(VisionFade(visionFadeTime, true));
        //Destroy(selectionCircle);
        //Destroy(miniMapCircle);
        //Destroy(navMeshAgent);
        //Destroy(GetComponent<Collider>()); //will make it unselectable on click
        //if (animator != null)
        //{
        //    Destroy(animator, 10f); //give it some time to complete the animation
        //}
        selectionCircle.enabled = false;
        miniMapCircle.enabled = false;
        navMeshAgent.enabled = false;
        GetComponent<Collider>().enabled = false; //will make it unselectable on click
        StartCoroutine(DecayIntoGround());
    }

    private IEnumerator DecayIntoGround()
    {
        yield return Yielders.Get(5f);
        float startY = transform.position.y;
        float depth = 2f;
        while (transform.position.y > startY - depth)
        {
            transform.position += Vector3.down * Time.deltaTime * 0.1f;
            yield return null;
        }
        Destroy(gameObject);
    }

    private IEnumerator VisionFade(float fadeTime, bool fadeOut)
    {
        Color newColor = visionCircle.material.color;
        float deadline = Time.time + fadeTime;
        while (Time.time < deadline)
        {
            //newColor = sightCircle.material.color;
            newColor.a = newColor.a + Time.deltaTime * fadeTime * -fadeOut.ToSignFloat();
            visionCircle.material.color = newColor;
            yield return null;
        }
        if (fadeOut)
        {
            Destroy(visionCircle);
        }
    }

    private IEnumerator HideSeenThings(float fadeTime)
    {
        if (fadeTime != 0f)
        {
            yield return Yielders.Get(fadeTime);
        }

        float radius = template.guardDistance;
        template.guardDistance = 0f;
        fieldOfView.MarkTargetsVisibility();
        template.guardDistance = radius;
    }

    private Unit[] GetNearestHostileUnits()
    {
        hostiles = FindObjectsOfType<Unit>().Where(unit => !FactionTemplate.IsAlliedWith(unit.faction, faction)).Where(unit => Vector3.Distance(unit.transform.position, transform.position) < template.guardDistance).ToArray();

        //TODO: sort array by distance
        return hostiles;
    }

    private Unit GetNearestHostileUnit()
    {
        hostiles = FindObjectsOfType<Unit>().Where(unit => !FactionTemplate.IsAlliedWith(unit.faction, faction)).ToArray();

        Unit nearestEnemy = null;
        float nearestEnemyDistance = float.PositiveInfinity;
        for (int i = 0; i < hostiles.Count(); i++)
        {
            if (IsDeadOrNull(hostiles[i]))
            {
                continue;
            }

            float distanceFromHostile = Vector3.Distance(hostiles[i].transform.position, transform.position);
            if (distanceFromHostile <= template.guardDistance)
            {
                if (distanceFromHostile < nearestEnemyDistance)
                {
                    nearestEnemy = hostiles[i];
                    nearestEnemyDistance = distanceFromHostile;
                }
            }
        }

        return nearestEnemy;
    }

    public void SetSelected(bool selected)
    {
        //Set transparency dependent on selection

        GameManager gameManager = GameManager.Instance;
        Color newColor;
        if (faction == gameManager.playerFaction)
        {
            newColor = Color.green;
        }
        else if (FactionTemplate.IsAlliedWith(faction, gameManager.playerFaction))
        {
            newColor = Color.yellow;
        }
        else
        {
            newColor = Color.red;
        }
        miniMapCircle.material.color = newColor;
        newColor.a = (selected) ? 1f : .3f;
        selectionCircle.material.color = newColor;
    }

    public void SetVisibility(bool visibility)
    {
        if (visibility)
        {
            if (visible)
            {
                return;
            }
        }
        else
        {
            if (!visible)
            {
                return;
            }
        }

        visible = visibility;

        IEnumerable<GameObject> parts = GetComponentsInChildren<Transform>().Where(form =>
            form.gameObject.layer == layerDefaultVisible ||
            form.gameObject.layer == layerDefaultHidden ||
            form.gameObject.layer == layerMiniMapVisible ||
            form.gameObject.layer == layerMiniMapHidden
        ).Select(form => form.gameObject);

        foreach (GameObject part in parts)
        {
            if (part.layer == layerDefaultVisible || part.layer == layerDefaultHidden)
            {
                if (visibility)
                {
                    part.layer = layerDefaultVisible;
                }
                else
                {
                    part.layer = layerDefaultHidden;
                }
            }
            else
            {
                if (visibility)
                {
                    part.layer = layerMiniMapVisible;
                }
                else
                {
                    part.layer = layerMiniMapHidden;
                }
            }
        }

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

    private void UpdateMinimapUI()
    {
        GameManager gameManager = GameManager.Instance;
        UIManager uiManager = UIManager.Instance;
        Material minimapCircleMaterial = miniMapCircle.material;
        switch (uiManager.minimapColoringMode)
        {
            case UIManager.MinimapColoringModes.FriendFoe:
                if (faction == gameManager.playerFaction)
                {
                    minimapCircleMaterial.color = Color.green;
                }
                else if (FactionTemplate.IsAlliedWith(faction, gameManager.playerFaction))
                {
                    minimapCircleMaterial.color = Color.yellow;
                }
                else
                {
                    minimapCircleMaterial.color = Color.red;
                }
                break;
            case UIManager.MinimapColoringModes.Teamcolor:
                minimapCircleMaterial.color = faction.color;
                break;
        }
    }

    public void AdjustModelAngleToGround()
    {
        Ray ray = new Ray(modelHolder.position + Vector3.up * 0.1f, Vector3.down);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 1f, InputManager.Instance.groundLayerMask))
        {
            Quaternion newRotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * modelHolder.parent.rotation;
            modelHolder.rotation = Quaternion.Lerp(modelHolder.rotation, newRotation, Time.deltaTime * 8f);
            selectionCircle.transform.rotation = modelHolder.rotation;
        }
    }

    public bool SetCombatReady(bool state)
    {
        const string name = "DoCombatReady";
        foreach (var parameter in animator.parameters)
        {
            if (parameter.name == name)
            {
                float value = animator.GetFloat(name);
                float stat = state.ToFloat();
                if (value != stat)
                {
                    if (LerpingCombatReady != null)
                    {
                        StopCoroutine(LerpingCombatReady);
                    }
                    LerpingCombatReady = StartCoroutine(LerpCombatReadyAnim(state.ToFloat()));
                    return true;
                }
            }
        }
        return false;
    }

    private IEnumerator LerpCombatReadyAnim(float state)
    {
        const string name = "DoCombatReady";

        float value;
        for (; ; )
        {
            value = animator.GetFloat(name);
            value = Mathf.MoveTowards(value, state, Time.deltaTime * combatReadySwitchTime);
            animator.SetFloat(name, value);
            if (value != state)
            {
                yield return null;
            }
            else
            {
                LerpingCombatReady = null;
                yield break;
            }
        }
    }

    private void ShootProjectile(int damage)
    {
        if (template.projectile == null || template.projectile.GetComponent<Projectile>() == null)
        {
            Debug.LogError("This unit has no Projectile set", this);
            return;
        }
        if (projectileFirePoint == null)
        {
            Debug.LogError("This unit has no Projectile Fire Point set", this);
            return;
        }

        Projectile projectileInstance = Instantiate(template.projectile, projectileFirePoint.position, projectileFirePoint.rotation).GetComponent<Projectile>();
        projectileInstance.LaunchAt(targetOfAttack.transform, damage, this);
    }

    public float GetSelectionCircleSize()
    {
        return selectionCircle.transform.localScale.x;
    }
}