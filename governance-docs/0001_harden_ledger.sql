-- RPAS-TCL Phase 2: SQL-Native Hardening

CREATE OR REPLACE FUNCTION transition_task_state(
  p_task_id UUID,
  p_fencing_token UUID,
  p_to_status task_status
) RETURNS VOID AS $$
DECLARE
  v_current_status task_status;
  v_files JSONB;
  v_file TEXT;
  v_system_status TEXT;
BEGIN
  -- G1: Control Plane Health Check
  SELECT status INTO v_system_status FROM ledger_state LIMIT 1;
  IF v_system_status != 'ACTIVE' THEN
    RAISE EXCEPTION 'RPAS-G1: Control Plane is %', v_system_status;
  END IF;

  -- Fetch State & Lock Task Row
  SELECT status, requested_files INTO v_current_status, v_files
  FROM governance_tasks
  WHERE id = p_task_id AND fencing_token = p_fencing_token
  FOR UPDATE;

  IF NOT FOUND THEN
    RAISE EXCEPTION 'RPAS-G3: Invalid Task or Fencing Token';
  END IF;

  -- G2: Transition Matrix Enforcement
  IF v_current_status IN ('COMPLETED', 'ABORTED') THEN
    RAISE EXCEPTION 'RPAS-G2: Terminal states are immutable';
  END IF;

  -- Implementation of the legal transition matrix
  IF NOT (
    (v_current_status = 'DECLARED' AND p_to_status = 'ACCEPTED') OR
    (v_current_status = 'ACCEPTED' AND p_to_status = 'ACTIVE') OR
    (v_current_status = 'ACCEPTED' AND p_to_status = 'ABORTED') OR
    (v_current_status = 'ACTIVE' AND p_to_status = 'COMPLETED') OR
    (v_current_status = 'ACTIVE' AND p_to_status = 'ABORTED')
  ) THEN
    RAISE EXCEPTION 'RPAS-G2: Illegal transition % -> %', v_current_status, p_to_status;
  END IF;

  -- Phase 2.1: Native Lock Acquisition (On ACCEPTED)
  IF p_to_status = 'ACCEPTED' THEN
    FOR v_file IN SELECT jsonb_array_elements_text(v_files) LOOP
      -- CP2/COL-G3: Pessimistic check for active locks
      IF EXISTS (SELECT 1 FROM file_locks WHERE file_path = v_file AND expires_at > NOW()) THEN
        RAISE EXCEPTION 'COL-OVERWRITE: Collision on %', v_file;
      END IF;

      INSERT INTO file_locks (file_path, task_id, fencing_token, expires_at)
      VALUES (v_file, p_task_id, p_fencing_token, NOW() + INTERVAL '30 minutes')
      ON CONFLICT (file_path) DO UPDATE 
      SET task_id = EXCLUDED.task_id, fencing_token = EXCLUDED.fencing_token, expires_at = EXCLUDED.expires_at;
    END LOOP;
  END IF;

  -- Phase 2.2: Native Lock Reclamation (On Terminality)
  IF p_to_status IN ('COMPLETED', 'ABORTED') THEN
    DELETE FROM file_locks WHERE task_id = p_task_id;
    DELETE FROM task_heartbeats WHERE task_id = p_task_id;
  END IF;

  -- Update Status
  UPDATE governance_tasks SET status = p_to_status, updated_at = NOW() WHERE id = p_task_id;

  -- G3: Record Event
  INSERT INTO governance_events (task_id, to_status, agent_id, timestamp)
  SELECT p_task_id, p_to_status, agent_id, NOW()
  FROM governance_tasks WHERE id = p_task_id;

END;
$$ LANGUAGE plpgsql;