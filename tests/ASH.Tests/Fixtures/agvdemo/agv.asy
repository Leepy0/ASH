VERSION 12.6.1.19
SYSTYPE AGVS
UNITS Feet Seconds
SYSDEF timeout 60 Seconds confname Config1
FLAGS
	System Inherit
	Text Invisible Inherit
	Paths Inherit
	Path Names Invisible Inherit
	Direction Invisible Inherit
	Vehicles Inherit
	Path Attach Points Invisible Inherit
	Control Points Invisible Inherit
	Control Point Names Invisible Inherit
	Transfers Invisible Inherit
AGVSDEF secname path31 name workcheck1 UserId 2
	NEXTPATH name path31 type DefaultGuidePath
	NEXTCP name workcheck1 type DefaultControlPoint
	ALTERNATE NONE ignore ResumeSpeedWhenClaimed
	vel 0 Infinite Feet Seconds
	crabvel 0 Infinite Feet Seconds
	sprvel 0 Infinite Feet Seconds
	crvvel 0 Infinite Feet Seconds
AGVSTOL minang 450 maxang 1350
GPATHTYPE name DefaultGuidePath one normal attach rigid color -1 nav 1 vel 1 0 Feet Seconds 
GPATH name path11 type DefaultGuidePath piece begx -50 begy 50 endx 11 endy 50 upz 1
GPATH name path11_1 type DefaultGuidePath piece begx 4 begy 42 endx -50 endy 42 upz 1
GPATH name path12 type DefaultGuidePath piece begx 15 begy 46 endx 15 endy -30 upz 1
GPATH name path13 type DefaultGuidePath piece begx 8 begy -30 endx 8 endy 38 upz 1
GPATH name path15 type DefaultGuidePath piece cenx -50 ceny 45 begx -50 begy 42 upz 1 angle -900
GPATH name path16 type DefaultGuidePath piece cenx -50 ceny 47 begx -53 begy 47 upz 1 angle -900
GPATH name path17 type DefaultGuidePath piece cenx 11 ceny 46 begx 11 begy 50 upz 1 angle -900
GPATH name path18 type DefaultGuidePath piece cenx 4 ceny 38 begx 8 begy 38 upz 1 angle 900
GPATH name path20 type DefaultGuidePath piece cenx 11.5 ceny -30 begx 15 begy -30 upz 1 angle -1800
GPATH name path21 type DefaultGuidePath spur crab piece begx -45 begy 50 endx -45 endy 55 upz 1
GPATH name path21_1 type DefaultGuidePath spur crab piece begx -35 begy 50 endx -35 endy 55 upz 1
GPATH name path21_2 type DefaultGuidePath spur crab piece begx -25 begy 50 endx -25 endy 55 upz 1
GPATH name path21_3 type DefaultGuidePath spur crab piece begx -15 begy 50 endx -15 endy 55 upz 1
GPATH name path21_4 type DefaultGuidePath spur crab piece begx -5 begy 50 endx -5 endy 55 upz 1
GPATH name path22 type DefaultGuidePath spur crab piece begx 15 begy 0.0 endx 22 endy 0.0 upz 1
GPATH name path22_1 type DefaultGuidePath spur crab piece begx 15 begy -10 endx 22 endy -10 upz 1
GPATH name path22_2 type DefaultGuidePath spur crab piece begx 15 begy -20 endx 22 endy -20 upz 1
GPATH name path23 type DefaultGuidePath spur crab piece begx 8 begy -26 endx 2 endy -26 upz 1
GPATH name path23_1 type DefaultGuidePath spur crab piece begx 8 begy -16 endx 2 endy -16 upz 1
GPATH name path23_2 type DefaultGuidePath spur crab piece begx 8 begy -6 endx 2 endy -6 upz 1
GPATH name path30 type DefaultGuidePath piece begx -53 begy 45 endx -53 endy 47.1676410761155 upz 1
CPOINTTYPE name DefaultControlPoint cap 1 release distance 0 Feet align leadingpap limit Infinite scale 1 color -1 nrot 0 nscale 1
CPOINT name p_1 type DefaultControlPoint at path21 4.5
CPOINT name p_2 type DefaultControlPoint at path21_1 4.5
CPOINT name p_3 type DefaultControlPoint at path21_2 4.5
CPOINT name p_4 type DefaultControlPoint at path21_3 4.5
CPOINT name p_5 type DefaultControlPoint at path21_4 4.5
CPOINT name d_1 type DefaultControlPoint at path22 7
CPOINT name d_2 type DefaultControlPoint at path22_1 7
CPOINT name d_3 type DefaultControlPoint at path22_2 7
CPOINT name d_4 type DefaultControlPoint at path23 6
CPOINT name d_5 type DefaultControlPoint at path23_1 6
CPOINT name d_6 type DefaultControlPoint at path23_2 6
CPOINT name z1 type DefaultControlPoint at path11_1 50
CPOINT name z2 type DefaultControlPoint at path11_1 40
CPOINT name z3 type DefaultControlPoint at path11_1 27
CPOINT name z4 type DefaultControlPoint at path11_1 13
CPOINT name z5 type DefaultControlPoint at path11 10
CPOINT name z6 type DefaultControlPoint at path11 20
CPOINT name z7 type DefaultControlPoint at path11 30
CPOINT name z8 type DefaultControlPoint at path11 40
CPOINT name z9 type DefaultControlPoint at path11 53
CPOINT name z10 type DefaultControlPoint at path18 5.30327065467213
CPOINT name z11 type DefaultControlPoint at path13 56
CPOINT name z12 type DefaultControlPoint at path12 9
CPOINT name z13 type DefaultControlPoint at path13 39
CPOINT name z14 type DefaultControlPoint at path12 28
CPOINT name z15 type DefaultControlPoint at path13 19
CPOINT name z16 type DefaultControlPoint at path13 9
CPOINT name z17 type DefaultControlPoint at path20 5.06254466286847
CPOINT name z18 type DefaultControlPoint at path12 72
CPOINT name z19 type DefaultControlPoint at path12 61
CPOINT name z20 type DefaultControlPoint at path12 51
CPOINT name workcheck type DefaultControlPoint align leadingpap at path30 1.10268372703412
NAMELST name nlwork
NAMELST name nlwork item p_1 item p_2 item p_3 item p_4 item p_5 
PARKLST name d_1 Sequential item workcheck 
WORKLST name workcheck Oldest item nlwork 
PARKLST name d_2 Sequential item workcheck 
PARKLST name d_3 Sequential item workcheck 
PARKLST name d_4 Sequential item workcheck 
PARKLST name d_5 Sequential item workcheck 
PARKLST name d_6 Sequential item workcheck 
AGVVEHSEG name DefSegment cap 1 pickup 0 Seconds setdown 0 Seconds
	figcurspeed 100
	figmaxspeed 100
	 display picpos begx -20 begy -10 endx -19 endy -10

	template Feet
700 1
9 9 0 1 0 agvK_cell
1
700 21
9 9 0 1 0 AGVset
0  0  2.5
0 0 0 90
	1.00	1.00	1.00
2
700 17
9 9 0 1 0 Geom1
6
310 21
9 9 0 1 0 Base1
0  0  -0.5
0	0.00	0.00	0.00
	1.00	1.00	1.00
4.5 3 4 2.5 0.5 0 0
310 21
9 9 0 1 0 Base2
0  0  -2.5
0	0.00	0.00	0.00
	1.00	1.00	1.00
5.5 4 5.5 4 2 0 0
700 21
9 9 0 1 0 Bump1
0  0  -2.5
0	0.00	0.00	0.00
	1.00	1.00	1.00
4
310 21
9 9 0 1 0 one
-2  0  0
0	0.00	0.00	0.00
	1.00	1.00	1.00
8 0.0750000029802322 8 0.0750000029802322 0.800000011920929 0 0
310 21
9 9 0 1 0 three
-1.76999998092651  4.15999984741211  0
0 0 0 -52
	1.00	1.00	1.00
0.649999976158142 0.0750000029802322 0.649999976158142 0.0750000029802322 0.800000011920929 0 0
310 21
9 9 0 1 0 two
-1.76999998092651  -4.15999984741211  0
0 0 0 52
	1.00	1.00	1.00
0.649999976158142 0.0750000029802322 0.649999976158142 0.0750000029802322 0.800000011920929 0 0
310 21
9 9 0 1 0 four
0  4.34999990463257  0
0 0 0 90
	1.00	1.00	1.00
3.07999992370605 0.0750000029802322 3.07999992370605 0.0750000029802322 0.800000011920929 0 0
700 21
9 9 0 1 0 Bump2
0  0  -2.5
0 0 0 180
	1.00	1.00	1.00
4
310 21
9 9 0 1 0 one
-2  0  0
0	0.00	0.00	0.00
	1.00	1.00	1.00
8 0.0750000029802322 8 0.0750000029802322 0.800000011920929 0 0
310 21
9 9 0 1 0 three
-1.76999998092651  4.15999984741211  0
0 0 0 -52
	1.00	1.00	1.00
0.649999976158142 0.0750000029802322 0.649999976158142 0.0750000029802322 0.800000011920929 0 0
310 21
9 9 0 1 0 two
-1.76999998092651  -4.15999984741211  0
0 0 0 52
	1.00	1.00	1.00
0.649999976158142 0.0750000029802322 0.649999976158142 0.0750000029802322 0.800000011920929 0 0
310 21
9 9 0 1 0 four
0  4.34999990463257  0
0 0 0 90
	1.00	1.00	1.00
3.07999992370605 0.0750000029802322 3.07999992370605 0.0750000029802322 0.800000011920929 0 0
310 5
4 4 0 1 0 Base3_Blue
0  0  -0.870000004768372
0	0.00	0.00	0.00
	1.00	1.00	1.00
5.5 4 5.5 4 0.100000001490116 0 0
700 51
9 9 0 1 1 Lift_Joint
2
10 -0.33 0.45 0 0
0  0  0
0 0 0 0
1  1  1
0
1
310 17
9 9 0 1 1 Top
6 3.5 6 3.5 0.333000004291534 0 0
700 39
1 1 0 1 0 Link1
	0
	0	0	0	0	1
	0.00	0.00	0.00
0	0.00	0.00	0.00
	1.00	1.00	1.00
	0
0  0  0
0 0 0 0
1  1  1
0  0  0.349999994039536
0	0.00	0.00	0.00
	1.00	1.00	1.00
0
end
	fconn length 0.0 Feet noscale 0
	rconn length 0.0 Feet noscale 0
	PAPATTACH
	PAP ind 0 
		trx 0 try 0 trz 0
AGVSVEH type DefVehicle numveh 10
	vehsegs item DefSegment 
	start nlwork
	Stacking OTT_LDDISP	
	 display picpos begx -45.0009908136483 begy 54.3317683727034 endx -44.0009908136483 endy 54.3317683727034

	load Default 
		accel	0 1 Feet Seconds Seconds
		decel	0 1 Feet Seconds Seconds
		vel	0 3 Feet Seconds
		crvvel	0 3 Feet Seconds
		sprvel	0 3 Feet Seconds
		rvel	0 3 Feet Seconds
		rcrvvel	0 3 Feet Seconds
		rsprvel	0 3 Feet Seconds
		crabvel	0 3 Feet Seconds
		rotate	0 2 Seconds
		brakedist	0 4.5 Feet
		stopdist	0 0 Feet
	load Empty 
		accel	1 0 Feet Seconds Seconds
		decel	1 0 Feet Seconds Seconds
		vel	1 0 Feet Seconds
		crvvel	1 0 Feet Seconds
		sprvel	1 0 Feet Seconds
		rvel	1 0 Feet Seconds
		rcrvvel	1 0 Feet Seconds
		rsprvel	1 0 Feet Seconds
		crabvel	1 0 Feet Seconds
		rotate	1 0 Seconds
		brakedist	1 0 Feet
		stopdist	1 0 Feet
