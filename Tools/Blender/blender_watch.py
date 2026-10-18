# Run once in Blender (Scripting tab > Open > Run Script). It then runs any job dropped into
# Tools/BlenderJobs/job.py and writes the outcome to Tools/BlenderJobs/job_result.txt.
import bpy, os, traceback, time, io, contextlib
JOB_DIR = r"D:\MMORPG\My project\Assets\Tools\BlenderJobs"
JOB = os.path.join(JOB_DIR, "job.py")
OUT = os.path.join(JOB_DIR, "job_result.txt")
_state = {"mtime": None}

def _tick():
    try:
        if os.path.exists(JOB):
            m = os.path.getmtime(JOB)
            if m != _state["mtime"]:
                _state["mtime"] = m
                buf = io.StringIO()
                t0 = time.time()
                try:
                    with contextlib.redirect_stdout(buf):
                        code = open(JOB, encoding="utf-8").read()
                        exec(compile(code, JOB, "exec"), {"__name__": "__job__"})
                    status = "OK"
                except Exception:
                    status = "ERROR\n" + traceback.format_exc()
                with open(OUT, "w", encoding="utf-8") as f:
                    f.write("%s %.1fs job_mtime=%s\n%s" % (status, time.time() - t0, m, buf.getvalue()[-20000:]))
    except Exception:
        pass
    return 1.0

import builtins
if not getattr(builtins, "_mu_watch", False):
    bpy.app.timers.register(_tick, persistent=True)
    builtins._mu_watch = True
print("MU job watcher running:", JOB)
