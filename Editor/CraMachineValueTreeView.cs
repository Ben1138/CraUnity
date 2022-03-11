using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.IMGUI.Controls;
using UnityEditor;


public class CraMachineValueTreeView : TreeView
{
    CraMachineValue[] Values = null;
    Dictionary<CraHandle, CraMachineValueTreeItem> ValueHandleToItem = new Dictionary<CraHandle, CraMachineValueTreeItem>();

    private CraMachineValueTreeView(CraMachineValue[] values, TreeViewState state, MultiColumnHeader multicolumnHeader) : base(state, multicolumnHeader)
    {
        Values = values;
        showAlternatingRowBackgrounds = true;
        showBorder = true;
        Reload();
    }

    public static CraMachineValueTreeView Create(CraMachineValue[] values)
    {
        MultiColumnHeaderState headerState = new MultiColumnHeaderState(new MultiColumnHeaderState.Column[] 
        {
            new MultiColumnHeaderState.Column() 
            { 
                headerContent = new GUIContent("Name"), 
                minWidth = 100f,
                autoResize = true,
                canSort = true
            },
            new MultiColumnHeaderState.Column()
            {
                headerContent = new GUIContent("Type"),
                minWidth = 100f,
                autoResize = true,
                canSort = true
            },
            new MultiColumnHeaderState.Column()
            {
                headerContent = new GUIContent("Value"),
                minWidth = 100f,
                autoResize = true,
                canSort = true
            },
        });

        var header = new MultiColumnHeader(headerState);
        header.ResizeToFit();

        var treeView = new CraMachineValueTreeView(values, new TreeViewState(), header);
        treeView.rowHeight = 30f;
        return treeView;
    }

    protected override TreeViewItem BuildRoot()
    {
        Debug.Assert(Values != null);

        var root = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };
        int idCounter = 1;

        if (Values == null)
        {
            return root;
        }

        for (int vi = 0; vi < Values.Length; ++vi)
        {
            CraMachineValue machineValue = Values[vi];

            CraMachineValueTreeItem valueItem = new CraMachineValueTreeItem
            {
                id = idCounter++,
                displayName = machineValue.GetName(),
                Value = machineValue
            };

            ValueHandleToItem.Add(machineValue.Handle, valueItem);
            root.AddChild(valueItem);
        }

        SetupDepthsFromParentsAndChildren(root);
        return root;
    }

    protected override void RowGUI(RowGUIArgs args)
    {
        var item = args.item as CraMachineValueTreeItem;
        Debug.Assert(item != null);

        var triggered = CraMain.Instance.StateMachines.ChangedValues;
        while (triggered.Count > 0)
        {
            CraHandle valueHandle = triggered.Dequeue();
            ValueHandleToItem[valueHandle].ChangeTime = Time.realtimeSinceStartup + 0.2f;
        }

        Color tmp1 = GUI.backgroundColor;
        Color tmp2 = GUI.contentColor;
        if (item.ChangeTime > Time.realtimeSinceStartup)
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

    void CellGUI(Rect cellRect, CraMachineValueTreeItem item, int column, in RowGUIArgs args)
    {
        // Center the cell rect vertically using EditorGUIUtility.singleLineHeight.
        // This makes it easier to place controls and icons in the cells.
        CenterRectUsingSingleLineHeight(ref cellRect);

        if (!item.Value.IsValid())
        {
            EditorGUI.LabelField(cellRect, "INVALID VALUE!");
            return;
        }

        CraValueUnion value = item.Value.GetValue();
        switch (column)
        {
            case 0:
            {
                EditorGUI.LabelField(cellRect, args.label);
                break;
            }
            case 1:
            {
                EditorGUI.LabelField(cellRect, value.Type.ToString());
                break;
            }
            case 2:
            {
                string valueString = "UNKNOWN TYPE";
                switch (value.Type)
                {
                    case CraValueType.Int:
                        valueString = $"{value.ValueInt}";
                        break;
                    case CraValueType.Float:
                        valueString = $"{value.ValueFloat:n2}";
                        break;
                    case CraValueType.Bool:
                        valueString = $"{value.ValueBool}";
                        break;
                    case CraValueType.Trigger:
                        float lifeTime = item.Value.GetTriggerLifeTime();
                        float maxLifeTime = item.Value.GetTriggerMaxLifeTime();
                        valueString = $"{value.ValueBool} ({lifeTime:n2} / {maxLifeTime:n2})";
                        break;
                }

                EditorGUI.LabelField(cellRect, valueString);
                break;
            }
        }
    }
}

class CraMachineValueTreeItem : TreeViewItem
{
    public CraMachineValue Value;
    public float ChangeTime;
}