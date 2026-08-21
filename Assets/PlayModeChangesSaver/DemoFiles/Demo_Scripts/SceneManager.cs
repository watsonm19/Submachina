using UnityEngine;
using UnityEngine.SceneManagement;

namespace PlayModeChangesSaver.DemoFiles.Demo_Scripts
{
    public class SimpleSceneManager : MonoBehaviour
    {
        public void LoadScene01()
        {
            SceneManager.LoadScene("Scene01");
        }
        public void LoadScene02()
        {
            SceneManager.LoadScene("Scene02");
        }
    }
}
