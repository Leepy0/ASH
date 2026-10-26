SYSTYPE Conveyor
UNITS Feet Seconds
SYSDEF timeout 60 Seconds confname Config1
FLAGS
	System Inherit
	Text Invisible Inherit
	Sections GTemplate Inherit
	Section Names Invisible Inherit
	Direction Invisible Inherit
	Fixed Interval Invisible Inherit
	Loads Inherit
	Stations Invisible Inherit
	Station Names Invisible Inherit
	Transfers Invisible Inherit
	Photoeyes Invisible Inherit
	Photoeye Names Invisible Inherit
	Center Line Invisible Inherit
CONVDEF UserId 8
	NEXTSEC name s9 type DefaultSection width 2 Feet
	NEXTSTA name t9 type DefaultStation
	NEXTTRAN name tr9 type DefaultTransfer
	NEXTMOTOR name motor1 type DefaultMotor
	NEXTPHOTOEYE name photoeye1 type DefaultPhotoeye
	ALTERNATE NONE EXTRASECTIONWIDTH 0 Feet
CONVTOL minang 450 maxang 1350
CONVMOTORTYPE name DefaultMotor
CONVMOTOR name M_sec1 type DefaultMotor
CONVSECTIONTYPE name DefaultSection width 2 Feet color -1 motor M_sec1 vel 1 Feet Seconds acc 1 Feet Seconds Seconds dec 1 Feet Seconds Seconds accum load stopsize 1 0 Feet movesize 1 0 Feet inductsize 1 0 Feet nofixed align centered_in_interval attach rigid nav 1
CONVSECTION name roller type DefaultSection motor M_sec1 piece begx 254.060 begy 53.617 begz 2.5 endx 280.060 endy 53.617 endz 2.5 upz 1
CONVSECTION name belt type DefaultSection motor M_sec1 piece begx 254.060 begy 43.617 begz 2.5 endx 280.060 endy 43.617 endz 2.5 upz 1
CONVSTATIONTYPE name DefaultStation raise 0 Seconds lower 0 Seconds dist 0 Feet release norestriction align leading cap Infinite scale 1 color -1 nrot 0 nscale 1
CONVPHOTOEYETYPE name DefaultPhotoeye blocktimeout 5 Seconds cleartimeout 5 Seconds
CONVTRANSFERTYPE name DefaultTransfer inductsize 1 0 Feet aheadinductsize 1 0 Feet speedadjust Origin starttime 0 Seconds finishtime 0 Seconds style double movemethod movesection
GRAPHICS_TEMPLATE_DEF
<GRAPHICS_TEMPLATE_LIST>
	<GRAPHICS_TEMPLATE name="ASH_Roller_Green">
		<SOLID name="MySolid">
			<COLOR value="LtGray"/>
			<HEIGHT value="0.25" unit="Feet"/>
			<TRANSPARENCY value="9"/>
			<CHANGE_WIDTH value="0.1" unit="Feet"/>
		</SOLID>
		<LEG name="MyLeg">
			<COLOR value="Green"/>
			<SIDES value="BOTH"/>
			<SPACING value="4" unit="Feet"/>
			<LEG_THICKNESS value="0.25" unit="Feet"/>
			<LEG_WIDTH value="0.25" unit="Feet"/>
			<FLOOR_Z_VALUE value="0" unit="Feet"/>
		</LEG>
		<CURBING name="MyCurbing">
			<COLOR value="Green"/>
			<SIDES value="BOTH"/>
			<CURB_WIDTH value="0.25" unit="Feet"/>
			<CURB_HEIGHT value="0.5" unit="Feet"/>
		</CURBING>
		<ROLLER name="MyRoller">
			<COLOR value="LtGray"/>
			<RADIUS value="0.1" unit="Feet"/>
			<THICKNESS value="0.2" unit="Feet"/>
			<SPACING value="1" unit="Feet"/>
			<DETAIL value="COARSE"/>
		</ROLLER>
		<SOLID name="MySolid">
			<COLOR value="White"/>
			<HEIGHT value="0.25" unit="Feet"/>
			<TRANSPARENCY value="10"/>
		</SOLID>
		<SOLID_SHADOW name="MySolid_">
			<COLOR value="DkGray"/>
			<HEIGHT value="0.25" unit="Feet"/>
			<TRANSPARENCY value="8"/>
			<FLOOR_Z_VALUE value="0.01" unit="Feet"/>
		</SOLID_SHADOW>
	</GRAPHICS_TEMPLATE>
	<GRAPHICS_TEMPLATE name="class1">
		<SOLID name="MySolid">
			<COLOR value="LtGray"/>
			<HEIGHT value="0.25" unit="Feet"/>
			<TRANSPARENCY value="9"/>
			<CHANGE_WIDTH value="0.1" unit="Feet"/>
		</SOLID>
		<LEG name="MyLeg">
			<COLOR value="DkGray"/>
			<SIDES value="BOTH"/>
			<SPACING value="4" unit="Feet"/>
			<LEG_THICKNESS value="0.25" unit="Feet"/>
			<LEG_WIDTH value="0.25" unit="Feet"/>
			<FLOOR_Z_VALUE value="0" unit="Feet"/>
		</LEG>
		<CURBING name="MyCurbing">
			<COLOR value="Black"/>
			<SIDES value="BOTH"/>
			<CURB_WIDTH value="0.25" unit="Feet"/>
			<CURB_HEIGHT value="0.5" unit="Feet"/>
		</CURBING>
		<ROLLER name="MyRoller">
			<COLOR value="DkGray"/>
			<RADIUS value="0.1" unit="Feet"/>
			<THICKNESS value="0.2" unit="Feet"/>
			<SPACING value="1" unit="Feet"/>
			<DETAIL value="COARSE"/>
		</ROLLER>
		<SOLID name="MySolid">
			<COLOR value="White"/>
			<HEIGHT value="0.25" unit="Feet"/>
			<TRANSPARENCY value="10"/>
		</SOLID>
		<SOLID_SHADOW name="MySolid_">
			<COLOR value="DkGray"/>
			<HEIGHT value="0.25" unit="Feet"/>
			<TRANSPARENCY value="8"/>
			<FLOOR_Z_VALUE value="0.01" unit="Feet"/>
		</SOLID_SHADOW>
	</GRAPHICS_TEMPLATE>
	<GRAPHICS_TEMPLATE name="RAP_Belt_Roller_DkGreen">
		<SOLID name="Belt">
			<COLOR value="Black"/>
			<HEIGHT value="0.05" unit="Feet"/>
			<CHANGE_WIDTH value="-0.15" unit="Feet"/>
			<OFFSET_OBJECT value="0.15" unit="Feet"/>
		</SOLID>
		<SOLID name="Belt_Lt">
			<COLOR value="LtGray"/>
			<HEIGHT value="0.05" unit="Feet"/>
			<TRANSPARENCY value="8"/>
			<CHANGE_WIDTH value="-0.15" unit="Feet"/>
			<OFFSET_OBJECT value="0.15" unit="Feet"/>
		</SOLID>
		<SOLID name="Belt_Lower">
			<COLOR value="Black"/>
			<HEIGHT value="0.05" unit="Feet"/>
			<CHANGE_WIDTH value="-0.15" unit="Feet"/>
			<TRANSLATE z="-0.25" unit="Feet"/>
			<BREAK/>
			<OFFSET_OBJECT value="0.15" unit="Feet"/>
		</SOLID>
		<ROLLER name="Belt_Ends">
			<COLOR value="Black"/>
			<CHANGE_WIDTH value="-0.15" unit="Feet"/>
			<RADIUS value="0.15" unit="Feet"/>
			<THICKNESS value="0.3" unit="Feet"/>
			<SPACING value="100" unit="Feet"/>
			<DETAIL value="COARSE"/>
		</ROLLER>
		<ROLLER name="Belt_Ends_Lt">
			<COLOR value="LtGray"/>
			<TRANSPARENCY value="8"/>
			<CHANGE_WIDTH value="-0.15" unit="Feet"/>
			<RADIUS value="0.15" unit="Feet"/>
			<THICKNESS value="0.3" unit="Feet"/>
			<SPACING value="100" unit="Feet"/>
			<DETAIL value="COARSE"/>
		</ROLLER>
		<ROLLER name="Rollers">
			<COLOR value="LtGray"/>
			<TRANSLATE z="-0.05" unit="Feet"/>
			<BREAK/>
			<RADIUS value="0.1" unit="Feet"/>
			<THICKNESS value="0.2" unit="Feet"/>
			<SPACING value="1" unit="Feet"/>
			<DETAIL value="COARSE"/>
			<OFFSET_OBJECT value="0.15" unit="Feet"/>
		</ROLLER>
		<CURBING name="Curbing_Upper">
			<COLOR value="Green"/>
			<CHANGE_WIDTH value="0.05" unit="Feet"/>
			<TRANSLATE z="-0.03" unit="Feet"/>
			<BREAK/>
			<SIDES value="BOTH"/>
			<CURB_WIDTH value="0.14" unit="Feet"/>
			<CURB_HEIGHT value="0.05" unit="Feet"/>
		</CURBING>
		<CURBING name="Curbing_Upper_Dk">
			<COLOR value="Black"/>
			<TRANSPARENCY value="8"/>
			<CHANGE_WIDTH value="0.05" unit="Feet"/>
			<TRANSLATE z="-0.03" unit="Feet"/>
			<BREAK/>
			<SIDES value="BOTH"/>
			<CURB_WIDTH value="0.14" unit="Feet"/>
			<CURB_HEIGHT value="0.05" unit="Feet"/>
		</CURBING>
		<CURBING name="Curbing_Middle">
			<COLOR value="Green"/>
			<CHANGE_WIDTH value="0.05" unit="Feet"/>
			<TRANSLATE z="-0.08" unit="Feet"/>
			<BREAK/>
			<SIDES value="BOTH"/>
			<CURB_WIDTH value="0.105" unit="Feet"/>
			<CURB_HEIGHT value="1.17" unit="Feet"/>
		</CURBING>
		<CURBING name="Curbing_Middle_Dk">
			<COLOR value="DkGray"/>
			<TRANSPARENCY value="8"/>
			<CHANGE_WIDTH value="0.05" unit="Feet"/>
			<TRANSLATE z="-0.08" unit="Feet"/>
			<BREAK/>
			<SIDES value="BOTH"/>
			<CURB_WIDTH value="0.105" unit="Feet"/>
			<CURB_HEIGHT value="1.17" unit="Feet"/>
		</CURBING>
		<CURBING name="Curbing_Lower">
			<COLOR value="Green"/>
			<CHANGE_WIDTH value="0.05" unit="Feet"/>
			<TRANSLATE z="-1.25" unit="Feet"/>
			<BREAK/>
			<SIDES value="BOTH"/>
			<CURB_WIDTH value="0.14" unit="Feet"/>
			<CURB_HEIGHT value="0.05" unit="Feet"/>
		</CURBING>
		<CURBING name="Curbing_Lower_Dk">
			<COLOR value="Black"/>
			<TRANSPARENCY value="8"/>
			<CHANGE_WIDTH value="0.05" unit="Feet"/>
			<TRANSLATE z="-1.25" unit="Feet"/>
			<BREAK/>
			<SIDES value="BOTH"/>
			<CURB_WIDTH value="0.14" unit="Feet"/>
			<CURB_HEIGHT value="0.05" unit="Feet"/>
		</CURBING>
		<LEG name="Rapistan_Leg_Brace_cel">
			<COLOR value="Green"/>
			<CHANGE_WIDTH value="0.15" unit="Feet"/>
			<CHANGE_HEIGHT value="-0.05" unit="Feet"/>
			<SIDES value="BOTH"/>
			<SPACING value="5" unit="Feet"/>
			<LEG_THICKNESS value="0.2" unit="Feet"/>
			<LEG_WIDTH value="0.2" unit="Feet"/>
			<FLOOR_Z_VALUE value="0" unit="Feet"/>
			<OFFSET_OBJECT value="0.5" unit="Feet"/>
			<CELL_FILE unit="Feet">
700 17
-1 -1 0 1 1 module_leg_cel
1
700 21
-1 -1 0 1 1 module
	0.00	0.00	0.00
0 0 0 90
	1.00	1.00	1.00
7
700 21
-1 -1 0 1 1 FLOOR_WIDTH
2  0  1
0	0.00	0.00	0.00
	1.00	1.00	1.00
2
310 17
-1 -1 0 1 1 a
0.800000011920929 0.129999995231628 0.800000011920929 0.129999995231628 0.0500000007450581 0 0
310 1
0 0 0 1 9 a
0.800000011920929 0.129999995231628 0.800000011920929 0.129999995231628 0.0500000007450581 0 0
700 21
-1 -1 0 1 1 FLOOR_WIDTH
-2  0  1
0	0.00	0.00	0.00
	1.00	1.00	1.00
2
310 17
-1 -1 0 1 1 a
0.800000011920929 0.129999995231628 0.800000011920929 0.129999995231628 0.0500000007450581 0 0
310 1
0 0 0 1 9 a
0.800000011920929 0.129999995231628 0.800000011920929 0.129999995231628 0.0500000007450581 0 0
310 21
-1 -1 0 1 1 Zwidth
2  0  1
0 0 180 0
	1.00	1.00	1.00
0.400000005960464 0.100000001490116 0.400000005960464 0.100000001490116 2 0 0
310 21
-1 -1 0 1 1 Zwidth
-2  0  1
0 0 180 0
	1.00	1.00	1.00
0.400000005960464 0.100000001490116 0.400000005960464 0.100000001490116 2 0 0
310 5
0 0 0 1 9 Zwidth
2  0  1
0 0 180 0
	1.00	1.00	1.00
0.400000005960464 0.100000001490116 0.400000005960464 0.100000001490116 2 0 0
310 5
0 0 0 1 9 Zwidth
-2  0  1
0 0 180 0
	1.00	1.00	1.00
0.400000005960464 0.100000001490116 0.400000005960464 0.100000001490116 2 0 0
700 21
-1 -1 0 1 1 hytrol_brace_dkgrn_cel
	0.00	0.00	0.00
0 0 0 90
	1.00	1.00	1.00
1
700 21
-1 -1 0 1 1 brace
	0.00	0.00	0.00
0 0 0 90
	1.00	1.00	1.00
2
700 21
-1 -1 0 1 1 X_WIDTH
2  0  0
0 0 0 90
	1.00	1.00	1.00
1
700 21
-1 -1 0 1 1 setup
0  -0.0700000002980232  -1.75
0	0.00	0.00	0.00
	1.00	1.00	1.00
2
310 17
-1 -1 0 1 1 w
0.0299999993294477 0.899999976158142 0.0299999993294477 0.899999976158142 0.5 0 0
310 1
0 0 0 1 9 w
0.0299999993294477 0.899999976158142 0.0299999993294477 0.899999976158142 0.5 0 0
700 21
-1 -1 0 1 9 X_WIDTH
-2  0  0
0 0 0 90
	1.00	1.00	1.00
1
700 21
-1 -1 0 1 1 setup
0  0.0700000002980232  -1.75
0	0.00	0.00	0.00
	1.00	1.00	1.00
2
310 17
-1 -1 0 1 1 w
0.0299999993294477 0.899999976158142 0.0299999993294477 0.899999976158142 0.5 0 0
310 1
0 0 0 1 9 w
0.0299999993294477 0.899999976158142 0.0299999993294477 0.899999976158142 0.5 0 0
			</CELL_FILE>
		</LEG>
		<LEG name="Rapistan_Logo_cel">
			<COLOR value="Blue"/>
			<CHANGE_WIDTH value="0.261" unit="Feet"/>
			<CHANGE_HEIGHT value="-0.05" unit="Feet"/>
			<SIDES value="BOTH"/>
			<SPACING value="50" unit="Feet"/>
			<LEG_THICKNESS value="0.2" unit="Feet"/>
			<LEG_WIDTH value="0.2" unit="Feet"/>
			<FLOOR_Z_VALUE value="0" unit="Feet"/>
			<OFFSET_OBJECT value="2" unit="Feet"/>
			<CELL_FILE unit="Feet">
700 17
-1 -1 0 1 1 rapistan_logo_cel
1
700 21
-1 -1 0 1 1 logo_rapistan_cel
	0.00	0.00	0.00
0 0 0 90
	1.00	1.00	1.00
2
700 21
-1 -1 0 1 1 X_WIDTH
2  0  0
0	0.00	0.00	0.00
	1.00	1.00	1.00
1
700 21
-1 -1 0 1 1 position
0  0  -0.5
0 90 90 0
	1.00	1.00	1.00
3
310 21
-1 -1 0 1 1 outline
-0.0199999995529652  0  0.00100000004749745
0 -90 0 0
	1.00	1.00	1.00
0.00100000004749745 1 0.00100000004749745 1 0.25 0 0
140 5
7 7 0 2 1 text
-0.439999997615814  0.0700000002980232  0.0020000000949949
0	0.00	0.00	0.00
0.150000005960464  0.109999999403954  1
Rapistan
310 5
7 7 0 1 1 outline
-0.0199999995529652  -0.0500000007450581  0
0 -90 0 0
	1.00	1.00	1.00
0.00100000004749745 1.10000002384186 0.00100000004749745 1.10000002384186 0.349999994039536 0 0
700 21
-1 -1 0 1 1 X_WIDTH
-2  0  0
0	0.00	0.00	0.00
	1.00	1.00	1.00
1
700 21
-1 -1 0 1 1 position
0  0  -0.5
0 90 -90 0
	1.00	1.00	1.00
3
310 21
-1 -1 0 1 1 outline
0.0199999995529652  0  0.00100000004749745
0 -90 0 0
	1.00	1.00	1.00
0.00100000004749745 1 0.00100000004749745 1 0.25 0 0
140 5
7 7 0 2 1 text
-0.400000005960464  0.0700000002980232  0.0020000000949949
0	0.00	0.00	0.00
0.150000005960464  0.109999999403954  1
Rapistan
310 5
7 7 0 1 1 outline
0.0199999995529652  -0.0500000007450581  0
0 -90 0 0
	1.00	1.00	1.00
0.00100000004749745 1.10000002384186 0.00100000004749745 1.10000002384186 0.349999994039536 0 0
			</CELL_FILE>
		</LEG>
		<SOLID_SHADOW name="Belt_Shadow">
			<COLOR value="Black"/>
			<TRANSPARENCY value="8"/>
			<TWO_DIMENSION/>
			<CHANGE_WIDTH value="-0.15" unit="Feet"/>
			<FLOOR_Z_VALUE value="-0.1" unit="Feet"/>
			<INVISIBLE/>
		</SOLID_SHADOW>
		<CURBING_SHADOW name="Curbing_Shadow">
			<COLOR value="Black"/>
			<TRANSPARENCY value="8"/>
			<TWO_DIMENSION/>
			<CHANGE_WIDTH value="0.05" unit="Feet"/>
			<BREAK/>
			<SIDES value="BOTH"/>
			<CURB_WIDTH value="0.14" unit="Feet"/>
			<CURB_HEIGHT value="0.5" unit="Feet"/>
			<FLOOR_Z_VALUE value="-0.1" unit="Feet"/>
			<INVISIBLE/>
		</CURBING_SHADOW>
	</GRAPHICS_TEMPLATE>
</GRAPHICS_TEMPLATE_LIST>
PATH_GT name roller
	ATTACH_GT ASH_Roller_Green
PATH_GT name belt
	ATTACH_GT RAP_Belt_Roller_DkGreen
