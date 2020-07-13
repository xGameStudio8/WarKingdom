using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// All arround game state manager
/// </summary>
public class GameManager : Singleton<GameManager>
{
    public enum GameMode
    {
        Gameplay,
        Cutscene,
    }
    public enum SelectionOnType
    {
        Units,
        Buildings,
    }

#if UNITY_EDITOR
    public bool editorFrameRateLock30 = true;
    [Space]
#endif
    public GameMode gameMode = GameMode.Gameplay;
    public SelectionOnType selectionOnType = SelectionOnType.Units;

    [Preview]
    public FactionTemplate playerFaction;
    public List<FactionTemplate> factions;

    public int startResourceGold = 300;
    public int startResourceWood = 0;

    public Platoon selectedPlatoon { get; private set; }
    public ClickableObject selectedObject { get; private set; }
    private UnityEngine.Playables.PlayableDirector activeDirector;

    private bool showHealthbars;

    void Awake()
    {
        selectedPlatoon = GetComponent<Platoon>();
        Cursor.lockState = CursorLockMode.Confined;
#if UNITY_EDITOR
        if (editorFrameRateLock30)
        {
            Application.targetFrameRate = 30;//just to keep things "smooth" during presentations
        }
#endif
        if (playerFaction == null)
        {
            Debug.LogError("No player faction set", this);
        }

        foreach (FactionTemplate faction in factions)
        {
            faction.resourceGold = startResourceGold;
            faction.resourceWood = startResourceWood;
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        foreach (FactionTemplate faction in factions)
        {
            faction.units.Clear();
        }
    }

    private void ChangeSelectionOnType(ClickableObject unit)
    {
        if (unit is Unit)
        {
            selectionOnType = SelectionOnType.Units;
        }
        else
        {
            selectionOnType = SelectionOnType.Buildings;
        }
    }

    public void IssueCommand(AICommand cmd, bool followUpCommand)
    {
        selectedPlatoon.ExecuteCommand(cmd, followUpCommand);
    }

    public void SetWaypoint(Vector3 pos)
    {
        Rekruiting rekruiting = selectedObject.GetComponent<Rekruiting>();
        if (rekruiting != null)
        {
            rekruiting.SetWaypoint(pos);
        }
    }

    public int GetSelectionLength()
    {
        if (selectionOnType == SelectionOnType.Units)
        {
            return selectedPlatoon.units.Count;
        }
        else
        {
            return (selectedObject != null).ToInt();
        }
    }

    public Transform[] GetPlattoonTransforms()
    {
        return selectedPlatoon.units.Select(x => x.transform).ToArray();
    }

    public List<Unit> GetPlattoonUnits()
    {
        return selectedPlatoon.units;
    }

    public bool IsInsideSelection(ClickableObject newSelectedUnit)
    {
        if (newSelectedUnit is Unit)
        {
            return selectedPlatoon.IncludesUnit(newSelectedUnit as Unit);
        }
        else
        {
            return selectedObject == newSelectedUnit;
        }
    }

    public void AddToSelection(IList<Unit> newSelectedUnits)
    {
        if (selectionOnType != SelectionOnType.Units && GetSelectionLength() != 0)
        {
            return;
        }
        ChangeSelectionOnType(newSelectedUnits[0]);
        foreach (Unit newSelectedUnit in newSelectedUnits)
        {
            AddToPlattoon(newSelectedUnit);
        }
    }

    private bool AddToPlattoon(Unit newSelectedUnit)
    {
        if (selectionOnType != SelectionOnType.Units && GetSelectionLength() != 0)
        {
            return false;
        }
        if (selectedPlatoon.IncludesUnit(newSelectedUnit))
        {
            return false;
        }
        ChangeSelectionOnType(newSelectedUnit);
        selectedPlatoon.AddUnit(newSelectedUnit);
        newSelectedUnit.SetSelected(true);
        UIManager.Instance.AddToSelection(newSelectedUnit);
        return true;
    }

    public void SetSelection(IList<Unit> newSelectedUnits)
    {
        ClearSelection();
        ChangeSelectionOnType(newSelectedUnits[0]);
        AddToSelection(newSelectedUnits);
    }

    public void SetSelection(ClickableObject newSelectedUnit)
    {
        ClearSelection();
        ChangeSelectionOnType(newSelectedUnit);
        if (newSelectedUnit is Unit)
        {
            AddToPlattoon(newSelectedUnit as Unit);
        }
        else
        {
            selectedObject = newSelectedUnit;
            newSelectedUnit.SetSelected(true);
            UIManager.Instance.AddToSelection(newSelectedUnit);
        }
    }

    public void RemoveFromSelection(ClickableObject unitToRemove)
    {
        if (unitToRemove is Unit)
        {
            selectedPlatoon.RemoveUnit(unitToRemove as Unit);
            UIManager.Instance.RemoveFromSelection(unitToRemove as Unit);
        }
        else
        {
            if (selectedObject == unitToRemove)
            {
                ClearObject();
            }
            else
            {
                throw new NullReferenceException();
            }
        }
        unitToRemove.SetSelected(false);
    }

    public void ClearSelection()
    {
        ClearPlattoon();
        ClearObject();
    }

    private void ClearObject()
    {
        if (selectedObject != null)
        {
            selectedObject.SetSelected(false);
            selectedObject = null;

            UIManager.Instance.ClearSelection();
        }
    }

    private void ClearPlattoon()
    {
        if (selectedPlatoon.units.Count == 0)
        {
            return;
        }

        foreach (Unit unit in selectedPlatoon.units)
        {
            unit.SetSelected(false);
        }

        selectedPlatoon.Clear();

        UIManager.Instance.ClearSelection();
    }

    public void MoveSelectedUnitsTo(Vector3 pos, bool followUpCommand)
    {
        AICommand newCommand = new AICommand(AICommand.CommandType.MoveTo, pos);
        IssueCommand(newCommand, followUpCommand);
    }

    public void AttackMoveSelectedUnitsTo(Vector3 pos, bool followUpCommand)
    {
        AICommand newCommand = new AICommand(AICommand.CommandType.AttackMoveTo, pos);
        IssueCommand(newCommand, followUpCommand);
    }

    public void AttackTarget(ClickableObject targetUnit, bool followUpCommand)
    {
        AICommand newCommand = new AICommand(AICommand.CommandType.AttackTarget, targetUnit);
        IssueCommand(newCommand, followUpCommand);
    }

    public List<ClickableObject> GetAllUnits()
    {
        return ClickableObject.globalObjectsList;
    }

    public List<ClickableObject> GetAllVisibleUnits()
    {
        return ClickableObject.globalObjectsList.Where(unit => unit.visible).ToList();
    }

    //Called by the TimeMachine Clip (of type Pause)
    public void PauseTimeline(UnityEngine.Playables.PlayableDirector whichOne)
    {
        activeDirector = whichOne;
        activeDirector.Pause();
        gameMode = GameMode.Cutscene; //InputManager will be waiting for a spacebar to resume
    }

    //Called by the InputManager
    public void ResumeTimeline()
    {
        activeDirector.Resume();
        gameMode = GameMode.Gameplay;
    }

    public void QuitGame()
    {
        Debug.Log("quit game");
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
