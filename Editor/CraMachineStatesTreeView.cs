using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.IMGUI.Controls;
using UnityEditor;


public class CraMachineStatesTreeView : TreeView
{
    CraStateMachine Machine = CraStateMachine.None;

    static readonly Dictionary<CraConditionType, string> ConditionSymbols = new Dictionary<CraConditionType, string>
    {
        { CraConditionType.None,           "NONE"     },
        { CraConditionType.Equal,          "=="       },
        { CraConditionType.Greater,        ">"        },
        { CraConditionType.Less,           "<"        },
        { CraConditionType.GreaterOrEqual, ">="       },
        { CraConditionType.LessOrEqual,    "<="       },
        { CraConditionType.AnyFlag,        "Flag"     },
        { CraConditionType.AllFlags,       "Flags"    },
        { CraConditionType.Trigger,        "Trigger"  },
        { CraConditionType.TimeMin,        "Time >"   },
        { CraConditionType.TimeMax,        "Time <"   },
        { CraConditionType.IsFinished,     "Finished" },
    };

    HashSet<int> ExpandRecursiveItems = new HashSet<int>();


    private CraMachineStatesTreeView(CraStateMachine machine, TreeViewState state, MultiColumnHeader multicolumnHeader) : base(state, multicolumnHeader)
    {
        Machine = machine;
        showAlternatingRowBackgrounds = true;
        showBorder = true;
        Reload();
    }

    public static CraMachineStatesTreeView Create(CraStateMachine machine)
    {
        Debug.Assert(machine.IsValid());
        MultiColumnHeaderState headerState = new MultiColumnHeaderState(new MultiColumnHeaderState.Column[] 
        {
            new MultiColumnHeaderState.Column() 
            { 
                headerContent = new GUIContent("Name"), 
                minWidth = 100f,
                autoResize = true
            },
            new MultiColumnHeaderState.Column()
            {
                headerContent = new GUIContent("Value"),
                minWidth = 100f,
                autoResize = true
            },
        });

        var header = new MultiColumnHeader(headerState);
        header.ResizeToFit();

        var treeView = new CraMachineStatesTreeView(machine, new TreeViewState(), header);
        treeView.rowHeight = 30f;
        return treeView;
    }

    protected unsafe override TreeViewItem BuildRoot()
    {
        Debug.Assert(Machine.IsValid());

        var root = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };
        int idCounter = 1;

        CraLayer[] layers = Machine.GetLayers();
        for (int li = 0; li < layers.Length; ++li)
        {
            CraState[] states = layers[li].GetAllStates();

            CraStateMachineTreeItem layerItem = new CraStateMachineTreeItem 
            { 
                id = idCounter++, 
                displayName = $"Layer {li}", 
                Value = $"States: {states.Length}" 
            };
            root.AddChild(layerItem);

            for (int si = 0; si < states.Length; ++si)
            {
                ref CraState state = ref states[si];

                CraStateMachineTreeItem stateItem = new CraStateMachineTreeItem 
                { 
                    Type = CraStateMachineTreeItemType.State, 
                    id = idCounter++, 
                    displayName = $"[{state.Handle.Index}] {state.GetName()}",

                    Layer = layers[li],
                    State = state
                };
                layerItem.AddChild(stateItem);

                CraState syncState = state.GetSyncState();
                stateItem.AddChild(new CraStateMachineTreeItem
                {
                    id = idCounter++,
                    displayName = "Sync To",
                    Value = syncState.IsValid() ? $"[{syncState.Handle.Index}] {syncState.GetName()}" : "NO STATE"
                });

                CraPlayer player = state.GetPlayer();
                if (player.IsValid())
                {
                    stateItem.AddChild(new CraStateMachineTreeItem
                    {
                        id = idCounter++,
                        displayName = "Play Speed",
                        Type = CraStateMachineTreeItemType.PlayerSpeed,
                        State = state
                    });

                    CraClip clip = player.GetClip();
                    stateItem.AddChild(new CraStateMachineTreeItem
                    {
                        id = idCounter++,
                        displayName = "Clip",
                        Value = clip.IsValid() ? $"{clip.GetName()}" : "NONE"
                    });
                }

                CraTransition[] transitions = state.GetTransitions();
                for (int ti = 0; ti < transitions.Length; ++ti)
                {
                    CraTransitionData data = transitions[ti].GetData();
                    CraStateMachineTreeItem transitionItem = new CraStateMachineTreeItem
                    {
                        id = idCounter++,
                        displayName = $"Transition {ti}",
                        Value = data.Target.IsValid() ? $"[{data.Target.Handle.Index}] {data.Target.GetName()}" : "NO TARGET",
                        TransitionIndex = ti,
                    };

                    ExpandRecursiveItems.Add(transitionItem.id);
                    stateItem.AddChild(transitionItem);

                    CraConditionOr* ors = &data.Or0;
                    for (int oi = 0; oi < 10; ++oi)
                    {
                        List<CraStateMachineTreeItem> andItems = new List<CraStateMachineTreeItem>();
                        CraCondition* ands = &ors[oi].And0;
                        for (int ai = 0; ai < 10; ++ai)
                        {
                            ref CraCondition and = ref ands[ai];
                            if (and.Type == CraConditionType.None)
                            {
                                continue;
                            }

                            string inputName = and.Input.IsValid() ? and.Input.GetName() : "NO INPUT";
                            string conditionStr = "";
                            switch (and.Type)
                            {
                                case CraConditionType.Trigger:
                                {
                                    conditionStr = $"{inputName} Triggered";
                                    break;
                                }
                                case CraConditionType.TimeMin:
                                {
                                    conditionStr = $"State Time > {and.Compare.ValueFloat:n2}";
                                    break;
                                }
                                case CraConditionType.TimeMax:
                                {
                                    conditionStr = $"State Time < {and.Compare.ValueFloat:n2}";
                                    break;
                                }
                                case CraConditionType.IsFinished:
                                {
                                    conditionStr = $"State Finished";
                                    break;
                                }
                                default:
                                {
                                    string inputValue;
                                    switch (and.Compare.Type)
                                    {
                                        case CraValueType.Int:
                                        {
                                            inputValue = and.Compare.ValueInt.ToString();
                                            break;
                                        }
                                        case CraValueType.Float:
                                        {
                                            inputValue = $"{and.Compare.ValueFloat:n2}";
                                            break;
                                        }
                                        case CraValueType.Bool:
                                        {
                                            inputValue = and.Compare.ValueBool.ToString();
                                            break;
                                        }
                                        default:
                                        {
                                            inputValue = "UNKNOWN VALUE TYPE";
                                            break;
                                        }
                                    }
                                    conditionStr = $"{inputName} {ConditionSymbols[and.Type]} {inputValue}";
                                    break;
                                }
                            }

                            CraStateMachineTreeItem andItem = new CraStateMachineTreeItem
                            {
                                id = idCounter++,
                                displayName = $"And {ai}",
                                State = state,
                                Value = conditionStr,
                                TransitionIndex = ti,
                                OrIndex = oi,
                                AndIndex = ai
                            };

                            andItems.Add(andItem);
                        }

                        if (andItems.Count > 0)
                        {
                            CraStateMachineTreeItem orItem = new CraStateMachineTreeItem
                            {
                                id = idCounter++,
                                displayName = $"Or {oi}",
                                State = state,
                                Value = $"{andItems.Count} condition(s)",
                                TransitionIndex = ti,
                                OrIndex = oi,
                            };

                            if (ors[oi].CheckTimeMin != 0f || ors[oi].CheckTimeMax != 0f)
                            {
                                orItem.AddChild(new CraStateMachineTreeItem
                                {
                                    id = idCounter++,
                                    displayName = "Check Min",
                                    Value = ors[oi].CheckTimeMin.ToString()
                                });

                                orItem.AddChild(new CraStateMachineTreeItem
                                {
                                    id = idCounter++,
                                    displayName = "Check Max",
                                    Value = ors[oi].CheckTimeMax.ToString()
                                });
                            }

                            for (int aii = 0; aii < andItems.Count; ++aii)
                            {
                                orItem.AddChild(andItems[aii]);
                            }

                            transitionItem.AddChild(orItem);
                        }
                    }
                }
            }
        }

        SetupDepthsFromParentsAndChildren(root);

        return root;
    }

    protected override void SingleClickedItem(int id)
    {
        if (ExpandRecursiveItems.Contains(id))
        {
            SetExpandedRecursive(id, !IsExpanded(id));
        }
        else
        {
            SetExpanded(id, !IsExpanded(id));
        }
    }

    protected unsafe override void RowGUI(RowGUIArgs args)
    {
        var item = args.item as CraStateMachineTreeItem;
        Debug.Assert(item != null);

        bool isActive = item.Layer.GetActiveState() == item.State;
        if (!isActive && item.State.IsValid() && item.TransitionIndex >=0 && item.OrIndex >= 0 && item.AndIndex >= 0)
        {
            CraTransition[]    transitions  = item.State.GetTransitions();
            CraTransitionData  data         = transitions[item.TransitionIndex].GetData();
            CraConditionOr*    ors          = &data.Or0;
            CraCondition*      ands         = &ors[item.OrIndex].And0;

            isActive = ands[item.AndIndex].ConditionMet;
        }

        Color tmp1 = GUI.backgroundColor;
        Color tmp2 = GUI.contentColor;

        if (isActive)
        {
            GUI.backgroundColor = Color.green;
            GUI.contentColor = Color.green;
        }

        for (int i = 0; i < args.GetNumVisibleColumns(); ++i)
        {
            CellGUI(args.GetCellRect(i), item, args.GetColumn(i), in args);
        }

        GUI.backgroundColor = tmp1;
        GUI.contentColor = tmp2;
    }

    void CellGUI(Rect cellRect, CraStateMachineTreeItem item, int column, in RowGUIArgs args)
    {
        // Center the cell rect vertically using EditorGUIUtility.singleLineHeight.
        // This makes it easier to place controls and icons in the cells.
        CenterRectUsingSingleLineHeight(ref cellRect);
        cellRect.x     += GetContentIndent(item);
        cellRect.width -= GetContentIndent(item);

        if (column == 0)
        {
            EditorGUI.LabelField(cellRect, args.label);
        }
        else
        {
            Debug.Assert(column == 1);

            switch (item.Type)
            {
                case CraStateMachineTreeItemType.State:
                {
                    Debug.Assert(item.State.IsValid());
                    EditorGUILayout.BeginHorizontal();

                    Rect playbackRect = cellRect;
                    Rect loopRect     = cellRect;
                    Rect playStopRect = cellRect;
                    Rect activateRect = cellRect;

                    playbackRect.width -= 150f;
                    loopRect.x += cellRect.width - 140f;
                    loopRect.width = 20f;
                    playStopRect.x += cellRect.width - 110f;
                    playStopRect.width = 40f;
                    activateRect.x += cellRect.width - 60f;
                    activateRect.width = 60f;

                    CraPlayer player = item.State.GetPlayer();
                    if (player.IsValid())
                    {
                        CraPlayRange range = player.GetPlayRange();
                        float timeIn = player.GetTime();
                        float timeOut = EditorGUI.Slider(playbackRect, GUIContent.none, timeIn, range.MinTime, range.MaxTime);
                        if (Mathf.Abs(timeOut - timeIn) > float.Epsilon)
                        {
                            player.SetPlay(CraPlayMode.OneFrame);
                            player.SetTime(timeOut);
                        }
                        player.SetLooping(GUI.Toggle(loopRect, player.IsLooping(), ""));
                        if (!player.IsPlaying() && player.GetTime() <= range.MinTime)
                        {
                            if (GUI.Button(playStopRect, "Play"))
                            {
                                player.SetPlay();
                            }
                        }
                        else if (GUI.Button(playStopRect, player.IsPlaying() ? "Stop" : "Back"))
                        {
                            player.Reset();
                        }
                        if (GUI.Button(activateRect, "Activate"))
                        {
                            if (!player.IsPlaying())
                            {
                                player.Reset();
                            }
                            item.Layer.SetActiveState(item.State);
                        }
                    }
                    else
                    {
                        EditorGUI.LabelField(cellRect, "State has no Player!");
                    }

                    EditorGUILayout.EndHorizontal();
                    break;
                }

                case CraStateMachineTreeItemType.PlayerSpeed:
                {
                    CraPlayer player = item.State.GetPlayer();
                    if (player.IsValid())
                    {
                        float speedIn = player.GetPlaybackSpeed();
                        float speedOut = EditorGUI.Slider(cellRect, GUIContent.none, speedIn, 0f, 10f);
                        if (Mathf.Abs(speedOut - speedIn) > float.Epsilon)
                        {
                            player.SetPlaybackSpeed(speedOut);
                        }
                    }
                    break;
                }

                default:
                {
                    EditorGUI.LabelField(cellRect, item.Value);
                    break;
                }
            }
        }
    }
}

enum CraStateMachineTreeItemType
{
    None, State, PlayerSpeed
}

class CraStateMachineTreeItem : TreeViewItem
{
    public CraStateMachineTreeItemType Type;
    public CraLayer Layer;
    public CraState State;
    public int TransitionIndex = -1;
    public int OrIndex = -1;
    public int AndIndex = -1;

    public string Value;
}