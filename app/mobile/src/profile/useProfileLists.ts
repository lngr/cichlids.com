import { useCallback } from "react";
import type { PictureListItem, TankListItem } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { usePagedList } from "../hooks/usePagedList";

export function useProfileLists(profileId: number) {
  const fetchPictures = useCallback((offset: number, limit: number) => apiClient.profiles.listPictures(profileId, { offset, limit }), [profileId]);
  const fetchTanks = useCallback((offset: number, limit: number) => apiClient.profiles.listTanks(profileId, { offset, limit }), [profileId]);
  const pictures = usePagedList<PictureListItem>(fetchPictures, [profileId]);
  const tanks = usePagedList<TankListItem>(fetchTanks, [profileId]);
  return { pictures, tanks };
}
