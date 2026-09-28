# Terminology

## Playback strategy

Can be either random, chronological, chronological round-robin. 

random plays anything from the feeds in the station at random. 
chronological simply plays all entries in the station based on their publish date. 
chronological round-robin plays one entry per feed in a chronological order. 

## Station

A group of feeds as well as a playback strategy and information on the playhead and last played item

## Profile

An Account can have multiple profiles. For a Parent and their child for example. 

## Feed

A podcast feed RSS/Atom

# Structure

# Ohrbob.Web

## Description

A Web Frontend where users can register, login, manage their account, manage, profiles stations and feeds and play streams. 


We require an OIDC provider to be linked. We support multiple providers. 
Google, AppleID and Nextcloud.

We do not store passwords.

We use bootstrap for styling.

We use aspnetcore with MVC and Razor. 

# Ohrbob.Server.Shared

## Description

Package for shared classes between all server components. 

# Ohrbob.Admin.Api

## Description

A rest api that is presenting management endpoints for end-users AND administrators to manage their accounts, stations  and feeds. 
## Authentication

