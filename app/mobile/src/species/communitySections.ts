import type { SpeciesCommunity } from "@cichlids/client-core";

export interface CommunitySections {
  photos: boolean;
  tanks: boolean;
  partners: boolean;
  any: boolean;
}

/**
 * Which sections of the community side of a species page have data to show.
 */
export function communitySections(community: SpeciesCommunity): CommunitySections {
  const photos = community.photos.length > 0;
  const tanks = Number(community.tankCount) > 0;
  const partners = community.stockingPartners.length > 0;
  return { photos, tanks, partners, any: photos || tanks || partners };
}

export const storyRoute = (photo: { slug: string }) => `/gallery/${photo.slug}`;

export const tankRoute = (tank: { id: number | string }) => `/tanks/${tank.id}`;

export const partnerRoute = (species: { id: number | string; slug?: string | null }) => `/species/${species.slug ?? species.id}`;
