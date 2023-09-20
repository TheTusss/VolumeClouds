using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[SelectionBase]
public class CloudsBox : MonoBehaviour {
    [SerializeField] private bool defaultDisplay = true;
    [SerializeField] private Color color = Color.blue;

    private void OnDrawGizmos() {
        if (!defaultDisplay) return;
        Gizmos.color = color;
        Gizmos.DrawWireCube(transform.position, transform.localScale);
    }

    private void OnDrawGizmosSelected() {
        Gizmos.color = color;
        Gizmos.DrawWireCube(transform.position, transform.localScale);
    }
}