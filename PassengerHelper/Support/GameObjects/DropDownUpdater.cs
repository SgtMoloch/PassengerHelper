namespace PassengerHelper.Support.GameObjects;

using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(TMP_Dropdown))]
public class DropDownUpdater : MonoBehaviour
{
    private TMP_Dropdown _dropdown;

    private Coroutine _coroutine;

    private Func<int> _valueClosure = () => 0;

    private void OnEnable()
    {
        PrepareComponents();
        _coroutine = StartCoroutine(UpdateCoroutine());
    }

    private void OnDisable()
    {
        StopCoroutine(_coroutine);
        _coroutine = null;
    }

    private IEnumerator UpdateCoroutine()
    {
        WaitForSecondsRealtime wait = new WaitForSecondsRealtime(0.1f);
        while (true)
        {
            UpdateValue();
            yield return wait;
        }
    }

    private void UpdateValue()
    {
        int isOnWithoutNotify = _valueClosure();
        _dropdown.SetValueWithoutNotify(isOnWithoutNotify);
    }

    public void Configure(Func<int> valueClosure)
    {
        PrepareComponents();
        _valueClosure = valueClosure;
        UpdateValue();
    }

    private void PrepareComponents()
    {
        if (!(_dropdown != null))
        {
            _dropdown = GetComponent<TMP_Dropdown>();
        }
    }
}