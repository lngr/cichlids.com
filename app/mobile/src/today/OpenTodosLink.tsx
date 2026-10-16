import { useTranslation } from "react-i18next";
import { Button } from "../components/Button";
import { useMachineRouter } from "../navigation/NavigationProvider";
import { todoCountLabelKey } from "../todos/todoGroups";
import { useOpenTodoCount } from "./useOpenTodoCount";

/** A link to the to-do list with the number of open to-dos; absent while none is open. */
export function OpenTodosLink() {
  const { t } = useTranslation();
  const router = useMachineRouter();
  const open = useOpenTodoCount();
  const key = todoCountLabelKey(open);
  return key ? <Button testID="today-todos" variant="secondary" label={t(key, { total: open })} onPress={() => router.push("/todos")} /> : null;
}
