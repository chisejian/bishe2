using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class Player : MonoBehaviour
{

    private NavMeshAgent agent;
    // Start is called before the first frame update
    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetMouseButtonUp(0))
        {
            if (Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out var hit))
            {
                print(hit.collider.name);
                agent.isStopped = false;
                agent.SetDestination(hit.point);
            }
        }

        if (Input.GetKeyUp(KeyCode.Space))
        {
            agent.isStopped = true;
        }
    }
}
